using System.Reflection;
using System.Diagnostics;
using System.Text;
using Autofac;
using DCFApixels.DragonECS;
using DragonExtensions;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.ECS.Scheduling;
using Karpik.Engine.Shared.DragonECS;
using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Core;

public class EngineRunner : IEngineRunner, IStaticModuleRegistry
{
    private readonly ILoggerFactory _hostLoggerFactory;
    private readonly ILoggerFactory? _ownedHostLoggerFactory;
    private readonly ILogger<EngineRunner> _hostLogger;
    private ILogger<EngineRunner> _runtimeLogger;

    public EngineRunner() : this(HostLogging.CreateDefaultFactory(), true) { }

    public EngineRunner(ILoggerFactory hostLoggerFactory) : this(hostLoggerFactory, false) { }

    private EngineRunner(ILoggerFactory hostLoggerFactory, bool ownsFactory)
    {
        _hostLoggerFactory = hostLoggerFactory ?? throw new ArgumentNullException(nameof(hostLoggerFactory));
        _ownedHostLoggerFactory = ownsFactory ? hostLoggerFactory : null;
        _hostLogger = hostLoggerFactory.CreateLogger<EngineRunner>();
        _runtimeLogger = _hostLogger;
    }

    private readonly List<IModuleInstaller> _modules = new();
    private readonly HashSet<Type> _registeredTypes = [];
    private readonly Dictionary<Assembly, int> _assemblyLoadRanks = new();
    private readonly Dictionary<IModuleInstaller, ModuleRegistration> _moduleRegistrations = new();
    private int _nextAssemblyLoadRank;
    private int _nextRegistrationRank;
    private EcsPipeline? _pipeline;
    private readonly Time _time = new();
    private ClientFrameMetrics _clientFrameMetrics = new();
    private Application _application = null!;
    private IContainer? _engineContainer;
    private ILifetimeScope? _modSetScope;
    private ILifetimeScope? _simulationScope;
    private IServiceResolver? _serviceResolver;
    private IStaticRuntimeComposition? _staticComposition;
    private StaticEcsRegistryProviders? _staticEcsProviders;
    private AutofacStaticServiceRegistry? _staticServices;
    private ISystemAsyncDestroy[] _asyncDestroyers = [];
    private bool _setupPending;

    /// <summary>
    /// Test-only seam: invoked immediately before the Dynamic-mode attributed
    /// registration path runs. Static startup must never trigger it.
    /// </summary>
    internal event Action<ContainerBuilder, IReadOnlyList<Type>, ModuleScope>? AttributedServiceRegistrationProbe;
    
    // Runners
    private EcsMainThreadBeginRunner _mainThreadBeginRunner = null!;
    private EcsMainThreadFrameBeginRunner _mainThreadFrameBeginRunner = null!;
    private EcsBeginRunner _beginRunner = null!;
    private EcsFixedRunner _fixedRunner = null!;
    private EcsUpdateRunner _updateRunner = null!;
    private EcsUpdateScheduler _ecsUpdateScheduler = new();
    private EcsRenderPrepareRunner _renderPrepareRunner = null!;
    private EcsRenderPrepareScheduler _ecsRenderPrepareScheduler = new();
    private EcsLateRunner _lateRunner = null!;
    private EcsRenderRunner _renderRunner = null!;
    private FixedRunTicker _fixedRunTicker = null!;

    public EcsUpdateSchedulerMode UpdateSchedulerMode { get; set; } = EcsUpdateSchedulerMode.Parallel;

    public bool IsApplicationRunning => _application.IsRunning;

    public void RegisterTypes(Type[] types)
    {
        ArgumentNullException.ThrowIfNull(types);

        if (_setupPending || _engineContainer is not null)
        {
            throw new InvalidOperationException("Types cannot be registered after setup has started.");
        }

        foreach (var type in types)
        {
            _registeredTypes.Add(type);
            if (!_assemblyLoadRanks.ContainsKey(type.Assembly))
            {
                _assemblyLoadRanks.Add(type.Assembly, _nextAssemblyLoadRank++);
            }
        }

        foreach (IModuleInstaller moduleInstance in DynamicCompositionDiscovery.ActivateModuleInstallers(types))
        {
            RegisterModule(moduleInstance);
        }
    }

    public void Setup(Application application, MainThreadScheduler scheduler, Dictionary<string, byte[]>? hotReloadData = null)
    {
        Setup(application, scheduler, new ClientFrameMetrics(), hotReloadData);
    }

    public void Setup(Application application, MainThreadScheduler scheduler, ClientFrameMetrics clientFrameMetrics, Dictionary<string, byte[]>? hotReloadData = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(clientFrameMetrics);

        if (_setupPending || _engineContainer is not null)
        {
            throw new InvalidOperationException("Runner is already set up or setup is pending.");
        }

        _application = application;
        _clientFrameMetrics = clientFrameMetrics;
        _setupPending = true;

        scheduler.Schedule(() =>
        {
            try
            {
                Task setup = SetupCoreAsync(application, scheduler, clientFrameMetrics, hotReloadData);
                if (!setup.IsCompleted)
                {
                    throw new InvalidOperationException(
                        "Asynchronous system initialization requires SetupAsync to be awaited by the host.");
                }
                setup.GetAwaiter().GetResult();
            }
            catch
            {
                DestroyAsyncCore(clearRegistrations: false).AsTask().GetAwaiter().GetResult();
                throw;
            }
            finally
            {
                _setupPending = false;
            }
        });
    }

    public Task SetupAsync(Application application, MainThreadScheduler scheduler, Dictionary<string, byte[]>? hotReloadData = null)
    {
        return SetupAsync(application, scheduler, new ClientFrameMetrics(), hotReloadData);
    }

    public Task SetupAsync(Application application, MainThreadScheduler scheduler, ClientFrameMetrics clientFrameMetrics, Dictionary<string, byte[]>? hotReloadData = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(clientFrameMetrics);

        if (_setupPending || _engineContainer is not null)
        {
            throw new InvalidOperationException("Runner is already set up or setup is pending.");
        }

        _application = application;
        _clientFrameMetrics = clientFrameMetrics;
        _setupPending = true;

        return scheduler.ScheduleAsync(async () =>
        {
            try
            {
                await SetupCoreAsync(application, scheduler, clientFrameMetrics, hotReloadData);
            }
            catch
            {
                await DestroyAsyncCore(clearRegistrations: false);
                throw;
            }
            finally
            {
                _setupPending = false;
            }
        });
    }

    private async Task SetupCoreAsync(
        Application application,
        MainThreadScheduler scheduler,
        ClientFrameMetrics clientFrameMetrics,
        Dictionary<string, byte[]>? hotReloadData)
    {
        SortModulesForVerification();

        bool isStatic = _staticServices is not null;

        var systemRegistry = new SystemRegistry();
        if (isStatic)
        {
            // Static mode registers ECS systems through generated typed factories
            // in the simulation scope (resolved decision #2); module Add-time
            // descriptors are still recorded, but Autofac type registration is skipped.
            systemRegistry.SuppressTypeRegistrations();
        }

        foreach (IModuleInstaller installer in _modules)
        {
            IModule? module = installer.CreateModule();
            if (module is not null)
            {
                module.Add(systemRegistry);
            }
        }

        Type[] orderedTypes = GetRegisteredTypesInDeterministicOrder();

        var engineBuilder = new ContainerBuilder();
        engineBuilder.RegisterInstance(application).AsSelf().SingleInstance();
        engineBuilder.RegisterInstance(scheduler).AsSelf().SingleInstance();
        engineBuilder.RegisterInstance(_time).AsSelf().SingleInstance();
        engineBuilder.RegisterInstance(clientFrameMetrics).AsSelf().SingleInstance();
        RegisterResolver(engineBuilder);
        if (isStatic)
        {
            _staticServices.Apply(engineBuilder, ModuleScope.Engine);
        }
        else
        {
            AttributedServiceRegistrationProbe?.Invoke(engineBuilder, orderedTypes, ModuleScope.Engine);
            AttributedServiceRegistrar.Register(engineBuilder, orderedTypes, ModuleScope.Engine);
        }
        RegisterInstallerServices(engineBuilder, ModuleScope.Engine);
        _engineContainer = engineBuilder.Build();
        _runtimeLogger = _engineContainer.ResolveOptional<ILogger<EngineRunner>>() ?? _hostLogger;

        _modSetScope = _engineContainer.BeginLifetimeScope(builder =>
        {
            RegisterResolver(builder);
            if (isStatic)
            {
                _staticServices.Apply(builder, ModuleScope.ModSet);
            }
            else
            {
                AttributedServiceRegistrationProbe?.Invoke(builder, orderedTypes, ModuleScope.ModSet);
                AttributedServiceRegistrar.Register(builder, orderedTypes, ModuleScope.ModSet);
            }
            RegisterInstallerServices(builder, ModuleScope.ModSet);
        });

        _simulationScope = _modSetScope.BeginLifetimeScope(builder =>
        {
            RegisterResolver(builder);
            if (isStatic)
            {
                _staticServices.Apply(builder, ModuleScope.Simulation);
            }
            else
            {
                AttributedServiceRegistrationProbe?.Invoke(builder, orderedTypes, ModuleScope.Simulation);
                AttributedServiceRegistrar.Register(builder, orderedTypes, ModuleScope.Simulation);
            }
            RegisterInstallerServices(builder, ModuleScope.Simulation);
            systemRegistry.RegisterTypes(builder);
        });
        _serviceResolver = _simulationScope.Resolve<IServiceResolver>();

        RestoreRestartWorkerState(_serviceResolver, hotReloadData);

        EcsPipeline.Builder pipelineBuilder = EcsPipeline.New();
        var moduleBuilder = new Builder(pipelineBuilder,
            _serviceResolver.GetService(typeof(ILogger<Builder>)) as ILogger<Builder>
            ?? _hostLoggerFactory.CreateLogger<Builder>());
        pipelineBuilder.AddModule(new JobSystemModule());
        pipelineBuilder
            .Layers.Add(CustomLayers.BEGIN_PROGRAM_LAYER).Before(EcsConsts.PRE_BEGIN_LAYER).Back
            .Layers.Add(CustomLayers.END_PROGRAM_LAYER).After(EcsConsts.POST_END_LAYER);

        systemRegistry.ResolveAndAdd(moduleBuilder, _serviceResolver);
        _pipeline = BuildPipeline(pipelineBuilder);
        _pipeline.Init();
        _mainThreadBeginRunner = _pipeline.GetRunner<EcsMainThreadBeginRunner>();
        _mainThreadFrameBeginRunner = _pipeline.GetRunner<EcsMainThreadFrameBeginRunner>();
        _beginRunner = _pipeline.GetRunner<EcsBeginRunner>();
        _fixedRunner = _pipeline.GetRunner<EcsFixedRunner>();
        _updateRunner = _pipeline.GetRunner<EcsUpdateRunner>();
        ConfigureEcsUpdateScheduler(_updateRunner);
        _lateRunner = _pipeline.GetRunner<EcsLateRunner>();
        _renderPrepareRunner = _pipeline.GetRunner<EcsRenderPrepareRunner>();
        ConfigureEcsRenderPrepareScheduler(_renderPrepareRunner);
        _renderRunner = _pipeline.GetRunner<EcsRenderRunner>();
        _fixedRunTicker = new FixedRunTicker(_fixedRunner, _application,
            _serviceResolver.GetService(typeof(ILogger<FixedRunTicker>)) as ILogger<FixedRunTicker>
            ?? _hostLoggerFactory.CreateLogger<FixedRunTicker>());

        _asyncDestroyers = moduleBuilder.AsyncDestroyers.ToArray();
        for (int index = 0; index < moduleBuilder.AsyncInitializers.Count; index++)
        {
            await moduleBuilder.AsyncInitializers[index].InitAsync(CancellationToken.None);
        }
    }

    public void Run(double dt)
    {
        RunMainThreadBegin();
        RunMainThreadFrameBegin();
        RunGameplayFrame(dt);
        RunRender();
    }

    public void RunMainThreadBegin()
    {
        long start = Stopwatch.GetTimestamp();
        _mainThreadBeginRunner.MainThreadBegin();
        _clientFrameMetrics.PublishMainThreadBegin(Stopwatch.GetTimestamp() - start);
    }

    public void RunMainThreadFrameBegin()
    {
        long start = Stopwatch.GetTimestamp();
        _mainThreadFrameBeginRunner.MainThreadFrameBegin();
        _clientFrameMetrics.PublishMainThreadFrameBegin(Stopwatch.GetTimestamp() - start);
    }

    public void RunGameplayFrame(double dt)
    {
        _time.Update(dt);
        _beginRunner.BeginRun();
        _pipeline!.Run();
        _fixedRunTicker.FixedRun();
        _ecsUpdateScheduler.Update();
        _lateRunner.LateRun();
        _ecsRenderPrepareScheduler.RenderPrepare();
    }

    public void RunRender()
    {
        long start = Stopwatch.GetTimestamp();
        _renderRunner.Render();
        _clientFrameMetrics.PublishRender(Stopwatch.GetTimestamp() - start);
    }

    public GameplayLoopDriver CreateGameplayLoopDriver()
    {
        if (_pipeline is null)
        {
            throw new InvalidOperationException("Runner must be set up before creating a gameplay loop driver.");
        }

        return new GameplayLoopDriver(
            _time,
            _pipeline,
            _beginRunner,
            _fixedRunner,
            _ecsUpdateScheduler,
            _lateRunner);
    }

    public EditorRuntimeSnapshot CaptureEditorSnapshot()
    {
        var world = _serviceResolver?.GetService(typeof(EcsDefaultWorld)) as EcsDefaultWorld;
        if (world is null || world.IsDestroyed)
        {
            return new EditorRuntimeSnapshot
            {
                CapturedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        var entities = new List<EditorEntitySnapshot>(Math.Min(world.Count, EditorSnapshotLimits.MaxEntities));
        var totalComponents = 0;
        var isTruncated = world.Count > EditorSnapshotLimits.MaxEntities;
        foreach (var entityId in world.Entities)
        {
            if (entities.Count == EditorSnapshotLimits.MaxEntities)
            {
                break;
            }

            var components = world.GetComponentsFor(entityId);
            int componentCount = Math.Min(
                components.Length,
                Math.Min(
                    EditorSnapshotLimits.MaxComponentsPerEntity,
                    EditorSnapshotLimits.MaxComponents - totalComponents));
            if (componentCount < components.Length)
            {
                isTruncated = true;
            }

            var componentSnapshots = new EditorComponentSnapshot[componentCount];
            for (var componentIndex = 0; componentIndex < componentCount; componentIndex++)
            {
                var component = components[componentIndex];
                string displayValue = FormatComponent(component);
                if (displayValue.Length > EditorSnapshotLimits.MaxDisplayValueLength)
                {
                    displayValue = displayValue[..EditorSnapshotLimits.MaxDisplayValueLength];
                    isTruncated = true;
                }

                componentSnapshots[componentIndex] = new EditorComponentSnapshot
                {
                    TypeName = component.GetType().FullName ?? component.GetType().Name,
                    DisplayValue = displayValue
                };
            }
            totalComponents += componentCount;

            entities.Add(new EditorEntitySnapshot
            {
                EntityId = entityId,
                Components = componentSnapshots
            });
        }

        return new EditorRuntimeSnapshot
        {
            CapturedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            TotalEntityCount = world.Count,
            IsTruncated = isTruncated,
            Entities = entities.ToArray()
        };
    }

    private static string FormatComponent(object component)
    {
        var type = component.GetType();
        var builder = new StringBuilder();
        var hasValue = false;

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            try
            {
                AppendMember(builder, field.Name, field.GetValue(component), ref hasValue);
            }
            catch (Exception ex)
            {
                AppendMember(builder, field.Name, FormatMemberError(ex), ref hasValue);
            }
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length == 0 && property.GetMethod is not null)
            {
                try
                {
                    AppendMember(builder, property.Name, property.GetValue(component), ref hasValue);
                }
                catch (Exception ex)
                {
                    AppendMember(builder, property.Name, FormatMemberError(ex), ref hasValue);
                }
            }
        }

        if (hasValue)
        {
            return builder.ToString();
        }

        try
        {
            return component.ToString() ?? type.Name;
        }
        catch (Exception ex)
        {
            return FormatMemberError(ex);
        }
    }

    private static void AppendMember(StringBuilder builder, string name, object? value, ref bool hasValue)
    {
        if (hasValue)
        {
            builder.AppendLine();
        }

        builder.Append(name);
        builder.Append(" = ");
        try
        {
            builder.Append(value?.ToString() ?? "null");
        }
        catch (Exception ex)
        {
            builder.Append(FormatMemberError(ex));
        }
        hasValue = true;
    }

    private static string FormatMemberError(Exception exception)
    {
        Exception error = exception is TargetInvocationException { InnerException: { } inner }
            ? inner
            : exception;
        return $"<error: {error.GetType().Name}: {error.Message}>";
    }

    public void Destroy()
    {
        DestroyAsyncCore(clearRegistrations: true).AsTask().GetAwaiter().GetResult();
    }

    public Task DestroyAsync() => DestroyAsyncCore(clearRegistrations: true).AsTask();

    public void LogJobError(Exception exception, ILogger fallback)
    {
        ILogger logger = _engineContainer is null ? fallback : _runtimeLogger;
        logger.LogError(exception, "Job failed");
    }

    public Dictionary<string, byte[]> GetHotReloadData()
    {
        IServiceResolver services = _serviceResolver
            ?? throw new InvalidOperationException("Runner must be set up before capturing restart-worker state.");

        Dictionary<string, byte[]> state = [];
        foreach (IRestartWorkerStateProvider provider in GetRestartWorkerStateProviders(services))
        {
            if (!state.TryAdd(provider.Key, provider.Capture()))
            {
                throw new InvalidOperationException(
                    $"More than one restart-worker state provider uses key '{provider.Key}'.");
            }
        }

        return state;
    }

    public List<IModuleInstaller> GetModules()
    {
        return _modules;
    }

    public void RegisterStaticComposition(IStaticRuntimeComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        if (_setupPending || _engineContainer is not null)
        {
            throw new InvalidOperationException("Modules cannot be registered after setup has started.");
        }

        _staticComposition = composition;
        var services = new AutofacStaticServiceRegistry();
        composition.RegisterServices(services);
        _staticServices = services;
        var providers = new StaticEcsRegistryProviders();
        composition.RegisterEcsRegistryProviders(providers);
        _staticEcsProviders = providers;
        composition.RegisterModules(this);
        // Freeze the generated installer order immediately so consumers of
        // GetModules() observe it before Setup runs.
        SortModulesForVerification();
    }

    void IStaticModuleRegistry.Add(IModuleInstaller installer) => RegisterModule(installer);

    public void RegisterModule(IModuleInstaller moduleInstaller)
    {
        ArgumentNullException.ThrowIfNull(moduleInstaller);

        if (_setupPending || _engineContainer is not null)
        {
            throw new InvalidOperationException("Modules cannot be registered after setup has started.");
        }

        if (moduleInstaller.GetType().GetCustomAttribute<ModuleAttribute>() is null)
        {
            throw new InvalidOperationException(
                $"Module installer '{moduleInstaller.GetType().FullName}' must have {nameof(ModuleAttribute)}.");
        }

        if (_modules.Any(m => m.GetType() == moduleInstaller.GetType()))
        {
            return;
        }

        _hostLogger.LogInformation("Register module {ModuleName}", moduleInstaller.Name);
        bool isStatic = _staticServices is not null;
        _modules.Add(moduleInstaller);
        Type installerType = moduleInstaller.GetType();
        _moduleRegistrations.Add(moduleInstaller, new ModuleRegistration(
            installerType.FullName ?? installerType.Name,
            // Same term the generator's InstallerOrder compares:
            // IAssemblyIdentity.GetDisplayName() on the compile side,
            // AssemblyName.FullName on the runtime side.
            installerType.Assembly.GetName().FullName ?? string.Empty,
            _nextRegistrationRank++,
            isStatic));
    }

    private static ModuleAttribute GetModuleAttribute(IModuleInstaller moduleInstaller)
    {
        return moduleInstaller.GetType().GetCustomAttribute<ModuleAttribute>()
               ?? throw new InvalidOperationException(
                   $"Module installer '{moduleInstaller.GetType().FullName}' has no {nameof(ModuleAttribute)}.");
    }

    /// <summary>
    /// Applies the module ordering comparator. Production entry points:
    /// <see cref="Setup"/> (Dynamic mode) and
    /// <see cref="RegisterStaticComposition"/> (Static freeze). Internal so
    /// parity tests can observe the Dynamic ordering without a full engine
    /// setup.
    /// </summary>
    internal void SortModulesForVerification() => _modules.Sort(CompareModules);

    private int CompareModules(IModuleInstaller left, IModuleInstaller right)
    {
        var leftRegistration = _moduleRegistrations[left];
        var rightRegistration = _moduleRegistrations[right];
        if (leftRegistration.IsStaticInsertion && rightRegistration.IsStaticInsertion)
        {
            // Static mode: preserve the generated insertion order exactly.
            return leftRegistration.RegistrationRank.CompareTo(rightRegistration.RegistrationRank);
        }

        // Canonical contract (aligned with the generator's InstallerOrder):
        // Scope asc -> Priority asc -> assembly identity -> full name. The
        // assembly term is the identity display name - not load rank - so the
        // Dynamic sequence equals the GENERATED emission for identical
        // selections regardless of runtime load order.
        var comparison = GetModuleAttribute(left).Scope.CompareTo(GetModuleAttribute(right).Scope);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = GetModuleAttribute(left).Priority.CompareTo(GetModuleAttribute(right).Priority);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.Ordinal.Compare(
            leftRegistration.AssemblyIdentity,
            rightRegistration.AssemblyIdentity);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.Ordinal.Compare(leftRegistration.TypeFullName, rightRegistration.TypeFullName);
        return comparison != 0
            ? comparison
            : leftRegistration.RegistrationRank.CompareTo(rightRegistration.RegistrationRank);
    }

    private Type[] GetRegisteredTypesInDeterministicOrder()
    {
        return _registeredTypes
            .OrderBy(type => _assemblyLoadRanks.GetValueOrDefault(type.Assembly, int.MaxValue))
            .ThenBy(type => type.FullName ?? type.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static void RegisterResolver(ContainerBuilder builder)
    {
        builder.Register(context =>
                new AutofacServiceResolver(context.Resolve<ILifetimeScope>()))
            .As<IServiceResolver>()
            .SingleInstance();
    }

    private void RegisterInstallerServices(ContainerBuilder builder, ModuleScope scope)
    {
        foreach (IModuleInstaller installer in _modules)
        {
            if (GetModuleAttribute(installer).Scope == scope)
            {
                _hostLogger.LogInformation("On Register Services for module {ModuleName}", installer.Name);
                installer.OnRegisterServices(builder);
            }
        }
    }

    private static IRestartWorkerStateProvider[] GetRestartWorkerStateProviders(IServiceResolver services)
    {
        IRestartWorkerStateProvider[] providers = services
            .ResolveAll<IRestartWorkerStateProvider>()
            .ToArray();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (IRestartWorkerStateProvider provider in providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Key))
            {
                throw new InvalidOperationException(
                    $"Restart-worker state provider '{provider.GetType().FullName}' has an empty key.");
            }

            if (!keys.Add(provider.Key))
            {
                throw new InvalidOperationException(
                    $"More than one restart-worker state provider uses key '{provider.Key}'.");
            }
        }

        return providers;
    }

    private void RestoreRestartWorkerState(
        IServiceResolver services,
        IReadOnlyDictionary<string, byte[]>? state)
    {
        IRestartWorkerStateProvider[] providers = GetRestartWorkerStateProviders(services);
        if (state is null || state.Count == 0)
        {
            return;
        }

        _runtimeLogger.LogInformation("Applying initial state from previous worker process");
        foreach (IRestartWorkerStateProvider provider in providers)
        {
            if (state.TryGetValue(provider.Key, out byte[]? data))
            {
                provider.Restore(data);
            }
        }
    }

    private async ValueTask DestroyAsyncCore(bool clearRegistrations)
    {
        EcsPipeline? pipeline = _pipeline;
        FixedRunTicker? fixedRunTicker = _fixedRunTicker;
        ILifetimeScope? simulationScope = _simulationScope;
        ILifetimeScope? modSetScope = _modSetScope;
        IContainer? engineContainer = _engineContainer;
        ISystemAsyncDestroy[] asyncDestroyers = _asyncDestroyers;

        _pipeline = null;
        _asyncDestroyers = [];
        _fixedRunTicker = null!;
        _serviceResolver = null;
        _simulationScope = null;
        _modSetScope = null;
        _engineContainer = null;

        List<Exception>? errors = null;
        void Record(Exception exception)
        {
            errors ??= [];
            errors.Add(exception);
        }

        try { _ecsUpdateScheduler.Dispose(); } catch (Exception exception) { Record(exception); }
        try { _ecsRenderPrepareScheduler.Dispose(); } catch (Exception exception) { Record(exception); }
        try { fixedRunTicker?.Destroy(); } catch (Exception exception) { Record(exception); }
        for (int index = asyncDestroyers.Length - 1; index >= 0; index--)
        {
            try { await asyncDestroyers[index].DestroyAsync(); } catch (Exception exception) { Record(exception); }
        }
        try { pipeline?.Destroy(); } catch (Exception exception) { Record(exception); }

        if (simulationScope is not null)
        {
            try { await simulationScope.DisposeAsync(); } catch (Exception exception) { Record(exception); }
        }
        if (modSetScope is not null)
        {
            try { await modSetScope.DisposeAsync(); } catch (Exception exception) { Record(exception); }
        }
        if (engineContainer is not null)
        {
            try { await engineContainer.DisposeAsync(); } catch (Exception exception) { Record(exception); }
        }

        _ecsUpdateScheduler = new EcsUpdateScheduler();
        _ecsRenderPrepareScheduler = new EcsRenderPrepareScheduler();

        if (clearRegistrations)
        {
            _modules.Clear();
            _registeredTypes.Clear();
            _moduleRegistrations.Clear();
            _assemblyLoadRanks.Clear();
            _staticComposition = null;
            _staticServices = null;
            _nextAssemblyLoadRank = 0;
            _nextRegistrationRank = 0;
        }

        _runtimeLogger = _hostLogger;
        if (clearRegistrations)
        {
            _ownedHostLoggerFactory?.Dispose();
        }

        if (errors is { Count: > 0 })
        {
            throw new AggregateException("One or more runner resources failed to shut down.", errors);
        }
    }

    private readonly record struct ModuleRegistration(
        string TypeFullName,
        string AssemblyIdentity,
        int RegistrationRank,
        bool IsStaticInsertion = false);

    private static EcsPipeline BuildPipeline(EcsPipeline.Builder newBuilder)
    {
        newBuilder.AddRunner<EcsMainThreadBeginRunner>();
        newBuilder.AddRunner<EcsMainThreadFrameBeginRunner>();
        newBuilder.AddRunner<EcsBeginRunner>();
        newBuilder.AddRunner<EcsFixedRunner>();
        newBuilder.AddRunner<EcsUpdateRunner>();
        newBuilder.AddRunner<EcsLateRunner>();
        newBuilder.AddRunner<EcsRenderPrepareRunner>();
        newBuilder.AddRunner<EcsRenderRunner>();
        return newBuilder.Build();
    }

    private void ConfigureEcsUpdateScheduler(EcsUpdateRunner updateRunner)
    {
        ISystemUpdate[] systems = ExtractUpdateSystems(updateRunner);
        EcsUpdateSystemDescriptor[] descriptors = _staticEcsProviders is not null
            ? _staticEcsProviders.CollectUpdateDescriptors()
            : DynamicCompositionDiscovery.CollectUpdateDescriptors(systems);
        _ecsUpdateScheduler.Initialize(systems, descriptors, UpdateSchedulerMode);
    }

    private static ISystemUpdate[] ExtractUpdateSystems(EcsUpdateRunner updateRunner)
    {
        var systems = new ISystemUpdate[updateRunner.Process.Length];
        for (int i = 0; i < updateRunner.Process.Length; i++)
        {
            if (updateRunner.Process[i] is not UpdateSystem updateSystem)
            {
                throw new InvalidOperationException(
                    $"Unsupported update process '{updateRunner.Process[i].GetType().FullName}'. " +
                    $"Register Karpik {nameof(ISystemUpdate)} systems through {nameof(Builder)}.");
            }

            systems[i] = updateSystem.System;
        }

        return systems;
    }

    private void ConfigureEcsRenderPrepareScheduler(EcsRenderPrepareRunner renderPrepareRunner)
    {
        ISystemRenderPrepare[] systems = ExtractRenderPrepareSystems(renderPrepareRunner);
        EcsUpdateSystemDescriptor[] descriptors = _staticEcsProviders is not null
            ? _staticEcsProviders.CollectRenderPrepareDescriptors()
            : DynamicCompositionDiscovery.CollectRenderPrepareDescriptors(systems);
        _ecsRenderPrepareScheduler.Initialize(systems, descriptors, UpdateSchedulerMode);
    }

    private static ISystemRenderPrepare[] ExtractRenderPrepareSystems(EcsRenderPrepareRunner renderPrepareRunner)
    {
        var systems = new ISystemRenderPrepare[renderPrepareRunner.Process.Length];
        for (int i = 0; i < renderPrepareRunner.Process.Length; i++)
        {
            if (renderPrepareRunner.Process[i] is not RenderPrepareSystem renderPrepareSystem)
            {
                throw new InvalidOperationException(
                    $"Unsupported render-prepare process '{renderPrepareRunner.Process[i].GetType().FullName}'. " +
                    $"Register Karpik {nameof(ISystemRenderPrepare)} systems through {nameof(Builder)}.");
            }

            systems[i] = renderPrepareSystem.System;
        }

        return systems;
    }

}
