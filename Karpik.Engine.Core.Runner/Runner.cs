using System.Reflection;
using System.Diagnostics;
using System.Text;
using DCFApixels.DragonECS;
using DragonExtensions;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.ECS.Scheduling;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Engine.Core;

public class EngineRunner : IEngineRunner
{
    private const string EcsHotReloadInstallerFullName = "Karpik.Engine.Shared.ECS.ECSInstaller";
    private readonly List<IInstaller> _modules = new();
    private readonly Dictionary<Assembly, int> _assemblyLoadRanks = new();
    private readonly Dictionary<IInstaller, ModuleRegistration> _moduleRegistrations = new();
    private int _nextAssemblyLoadRank;
    private int _nextRegistrationRank;
    private EcsPipeline _pipeline = null!;
    private Time _time = new();
    private ClientFrameMetrics _clientFrameMetrics = new();
    private EcsServiceProvider _serviceProvider = null!;
    private Application _application;
    
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
        foreach (var type in types)
        {
            if (!_assemblyLoadRanks.ContainsKey(type.Assembly))
            {
                _assemblyLoadRanks.Add(type.Assembly, _nextAssemblyLoadRank++);
            }
        }

        foreach (var type in FilterTypesToModules(types))
        {
            var moduleInstance = (IInstaller)Activator.CreateInstance(type)!;
            RegisterModule(moduleInstance);
        }
    }

    public void Setup(Application application, MainThreadScheduler scheduler, Dictionary<string, byte[]>? hotReloadData = null)
    {
        Setup(application, scheduler, new ClientFrameMetrics(), hotReloadData);
    }

    public void Setup(Application application, MainThreadScheduler scheduler, ClientFrameMetrics clientFrameMetrics, Dictionary<string, byte[]>? hotReloadData = null)
    {
        _application = application;
        _clientFrameMetrics = clientFrameMetrics;
        _serviceProvider = new EcsServiceProvider(new ServiceProvider());
        _serviceProvider.Register(scheduler);
        _serviceProvider.Register(_application);
        
        var newBuilder = EcsPipeline.New();
        var newModuleBuilder = new Builder(newBuilder);
        newBuilder.AddModule(new JobSystemModule());
        newBuilder
            .Layers.Add(CustomLayers.BEGIN_PROGRAM_LAYER).Before(EcsConsts.PRE_BEGIN_LAYER).Back
            .Layers.Add(CustomLayers.END_PROGRAM_LAYER).After(EcsConsts.POST_END_LAYER);
        _serviceProvider.Register(_time);
        _serviceProvider.Register(_clientFrameMetrics);
        
        newBuilder.Inject<IServiceContainer>(_serviceProvider);
        newBuilder.Inject<IServiceRegister>(_serviceProvider);
        newBuilder.Inject(_serviceProvider);

        scheduler.Schedule(() =>
        {
            _modules.Sort(CompareModules);

            RegisterServices(_serviceProvider);

            if (hotReloadData is { Count: > 0 })
            {
                Console.WriteLine("[Runner] Applying initial state from previous worker process");
                ApplyInitialState(_modules, _serviceProvider, hotReloadData);
            }
            
            ConfigureAndAddModule(_serviceProvider, newModuleBuilder);
            var newPipeline = BuildPipeline(newBuilder, _serviceProvider);
            AnotherModuleLoaded();

            _pipeline = newPipeline;
            InjectIntoSystems(newPipeline, _serviceProvider);
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
            _fixedRunTicker = new FixedRunTicker(_fixedRunner, _application);

            ConfigureComplete();
        });
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
        _pipeline.Run();
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
        var world = _serviceProvider.Get<EcsDefaultWorld>();
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
            builder.Append(", ");
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
        Destroy(GetModules());
        _ecsUpdateScheduler.Dispose();
        _ecsRenderPrepareScheduler.Dispose();
        _pipeline.Destroy();
        _pipeline = null;
        _modules.Clear();
        _moduleRegistrations.Clear();
        _assemblyLoadRanks.Clear();
        _nextAssemblyLoadRank = 0;
        _nextRegistrationRank = 0;
        _fixedRunTicker.Destroy();
        _ecsUpdateScheduler = new EcsUpdateScheduler();
        _ecsRenderPrepareScheduler = new EcsRenderPrepareScheduler();
    }

    public Dictionary<string, byte[]> GetHotReloadData()
    {
        return PreHotReload(GetModules(), _serviceProvider);
    }

    public List<IInstaller> GetModules()
    {
        return _modules;
    }

    public void RegisterModule(IInstaller installer)
    {
        if (_modules.Any(m => m.GetType() == installer.GetType()))
        {
            return;
        }

        Console.WriteLine($"Register module {installer.Name}");
        _modules.Add(installer);
        _moduleRegistrations.Add(installer, new ModuleRegistration(
            _assemblyLoadRanks.GetValueOrDefault(installer.GetType().Assembly, int.MaxValue),
            installer.GetType().FullName ?? installer.GetType().Name,
            _nextRegistrationRank++));
    }

    private Type[] FilterTypesToModules(Type[] types)
    {
        var classTypes = types.Where(t => t.IsClass && !t.IsAbstract);
        var moduleTypes = classTypes.Where(t => typeof(IInstaller).IsAssignableFrom(t) || typeof(IInstaller).IsAssignableTo(t));
        var withAttr = moduleTypes.Where(t => t.GetCustomAttribute<ModuleAttribute>() != null);
        return withAttr.ToArray();
    }
    
    private int GetPriority(IInstaller installer)
    {
        var attr = installer.GetType().GetCustomAttribute<ModuleAttribute>();
        return attr?.Priority ?? 0;
    }

    private int CompareModules(IInstaller left, IInstaller right)
    {
        var comparison = GetPriority(left).CompareTo(GetPriority(right));
        if (comparison != 0)
        {
            return comparison;
        }

        var leftRegistration = _moduleRegistrations[left];
        var rightRegistration = _moduleRegistrations[right];
        comparison = leftRegistration.AssemblyLoadRank.CompareTo(rightRegistration.AssemblyLoadRank);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.Ordinal.Compare(leftRegistration.TypeFullName, rightRegistration.TypeFullName);
        return comparison != 0
            ? comparison
            : leftRegistration.RegistrationRank.CompareTo(rightRegistration.RegistrationRank);
    }

    private Dictionary<string, byte[]> PreHotReload(List<IInstaller> oldModules, EcsServiceProvider newServiceProvider)
    {
        Dictionary<string, byte[]> hotReloadInfo = [];
        foreach (var oldModule in oldModules)
        {
            var name = oldModule.GetType().FullName ?? oldModule.GetType().Name;
            if (name != EcsHotReloadInstallerFullName)
            {
                continue;
            }

            if (oldModule is IInstallerHotReload oldModuleHotReload)
            {
                hotReloadInfo[name] = oldModuleHotReload.OnPrepareHotReload(newServiceProvider);
            }
        }

        return hotReloadInfo;
    }

    private List<IInstaller> CreateModules(Type[] allNewModuleTypes)
    {
        var newModuleInstances = new List<IInstaller>();
        foreach (var type in allNewModuleTypes)
        {
            newModuleInstances.Add((IInstaller)Activator.CreateInstance(type)!);
        }

        return newModuleInstances;
    }

    private void ApplyInitialState(List<IInstaller> modules, EcsServiceProvider serviceProvider, Dictionary<string, byte[]> stateData)
    {
        List<(IInstallerHotReload, byte[])> needToReload = [];
        
        foreach (var module in modules)
        {
            try
            {
                if (module is IInstallerHotReload hotReloadableModule)
                {
                    string name = module.GetType().FullName ?? module.GetType().Name;
                    if (stateData.TryGetValue(name, out var data))
                    {
                        Console.WriteLine($"On Hot Reload module {hotReloadableModule.Name}");
                        if (!hotReloadableModule.OnHotReload(data, serviceProvider))
                        {
                            needToReload.Add((hotReloadableModule, data));
                        }
                        Console.WriteLine($"[Runner] Applied initial state to module: {name}");
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Runner] Failed to apply initial state to module {module.GetType().FullName}: {e.Message}");
            }
        }
        
        foreach (var reload in needToReload)
        {
            try
            {
                Console.WriteLine($"On Hot Reload module {reload.Item1.Name}");
                reload.Item1.OnHotReload(reload.Item2, serviceProvider);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Runner] Failed to apply initial state on second pass: {e.Message}");
            }
        }
    }

    private void Destroy(List<IInstaller> oldModules)
    {
        for (var index = oldModules.Count - 1; index >= 0; index--)
        {
            if (oldModules[index] is IInstallerDestroy oldModule)
            {
                oldModule.Destroy();
            }
        }
    }

    private readonly record struct ModuleRegistration(int AssemblyLoadRank, string TypeFullName, int RegistrationRank);

    private void RegisterServices(EcsServiceProvider newServiceProvider)
    {
        foreach (var module in _modules)
        {
            Console.WriteLine($"On Register Services for module {module.Name}");
            module.OnRegisterServices(newServiceProvider, newServiceProvider);
        }
    }

    private void ConfigureAndAddModule(EcsServiceProvider newServiceProvider, IBuilder newBuilder)
    {
        foreach (var installer in _modules.OfType<IInstallerConfiguratable>())
        {
            Console.WriteLine($"On Configure module {installer.Name}");
            installer.OnConfigure(newServiceProvider, newServiceProvider, out IModule? module);
            if (module is not null)
            {
                Console.WriteLine($"Got module {module.GetType().Name}");
                module.Import(newBuilder);
            }
        }
    }

    private void ConfigureComplete()
    {
        foreach (var module in _modules.OfType<IInstallerConfiguratable>())
        {
            Console.WriteLine($"On Configure Complete {module.Name}");
            module.OnConfigureComplete(_serviceProvider);
        }
    }

    private void AnotherModuleLoaded()
    {
        var listeners = _modules.OfType<IInstallerListener>().ToArray();
        foreach (var module in _modules)
        {
            foreach (var listener in listeners)
            {
                Console.WriteLine($"On Another Module Loaded module {module.Name}. Listener {listener.Name}");
                listener.OnAnotherModuleLoaded(_serviceProvider, module, module.GetType().Assembly);
            }
        }
    }

    private EcsPipeline BuildPipeline(EcsPipeline.Builder newBuilder, EcsServiceProvider newServiceProvider)
    {
        newBuilder.AddRunner<EcsMainThreadBeginRunner>();
        newBuilder.AddRunner<EcsMainThreadFrameBeginRunner>();
        newBuilder.AddRunner<EcsBeginRunner>();
        newBuilder.AddRunner<EcsFixedRunner>();
        newBuilder.AddRunner<EcsUpdateRunner>();
        newBuilder.AddRunner<EcsLateRunner>();
        newBuilder.AddRunner<EcsRenderPrepareRunner>();
        newBuilder.AddRunner<EcsRenderRunner>();
        var newPipeline = newBuilder.Build(newServiceProvider);
        newServiceProvider.Register(newPipeline.Injector);
        newServiceProvider.Register(newPipeline);
        newServiceProvider.InjectAll();
        return newPipeline;
    }

    private void InjectIntoSystems(EcsPipeline newPipeline, EcsServiceProvider newServiceProvider)
    {
        foreach (var system in newPipeline.AllSystems)
        {
            newServiceProvider.Inject(system);
        }

        foreach (var system in newPipeline.AllRunners)
        {
            newServiceProvider.Inject(system.Value);
        }
    }

    private void ConfigureEcsUpdateScheduler(EcsUpdateRunner updateRunner)
    {
        ISystemUpdate[] systems = ExtractUpdateSystems(updateRunner);
        EcsUpdateSystemDescriptor[] descriptors = CollectEcsUpdateDescriptors(systems);
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
        EcsUpdateSystemDescriptor[] descriptors = CollectRenderPrepareDescriptors(systems);
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

    private static EcsUpdateSystemDescriptor[] CollectEcsUpdateDescriptors(ReadOnlySpan<ISystemUpdate> systems)
    {
        if (systems.Length == 0)
        {
            return [];
        }

        var assemblies = new HashSet<Assembly>();
        for (int i = 0; i < systems.Length; i++)
        {
            assemblies.Add(systems[i].GetType().Assembly);
        }

        var descriptors = new List<EcsUpdateSystemDescriptor>();
        foreach (Assembly assembly in assemblies)
        {
            AddProviderDescriptors(assembly, descriptors);
        }

        return descriptors.ToArray();
    }

    private static EcsUpdateSystemDescriptor[] CollectRenderPrepareDescriptors(
        ReadOnlySpan<ISystemRenderPrepare> systems)
    {
        if (systems.Length == 0)
        {
            return [];
        }

        var assemblies = new HashSet<Assembly>();
        for (int i = 0; i < systems.Length; i++)
        {
            assemblies.Add(systems[i].GetType().Assembly);
        }

        var descriptors = new List<EcsUpdateSystemDescriptor>();
        foreach (Assembly assembly in assemblies)
        {
            Type providerInterface = typeof(IEcsRenderPrepareRegistryProvider);
            foreach (Type type in assembly.GetTypes())
            {
                if (type.IsAbstract || !providerInterface.IsAssignableFrom(type))
                {
                    continue;
                }

                var provider = (IEcsRenderPrepareRegistryProvider?)Activator.CreateInstance(type, nonPublic: true);
                if (provider is null)
                {
                    throw new InvalidOperationException(
                        $"Unable to create ECS render-prepare registry provider '{type.FullName}'.");
                }

                ReadOnlySpan<EcsUpdateSystemDescriptor> providerDescriptors = provider.GetRenderPrepareSystems();
                for (int i = 0; i < providerDescriptors.Length; i++)
                {
                    descriptors.Add(providerDescriptors[i]);
                }
            }
        }

        return descriptors.ToArray();
    }

    private static void AddProviderDescriptors(
        Assembly assembly,
        List<EcsUpdateSystemDescriptor> descriptors)
    {
        Type providerInterface = typeof(IEcsUpdateRegistryProvider);
        foreach (Type type in assembly.GetTypes())
        {
            if (type.IsAbstract || !providerInterface.IsAssignableFrom(type))
            {
                continue;
            }

            var provider = (IEcsUpdateRegistryProvider?)Activator.CreateInstance(type, nonPublic: true);
            if (provider is null)
            {
                throw new InvalidOperationException(
                    $"Unable to create ECS update registry provider '{type.FullName}'.");
            }

            ReadOnlySpan<EcsUpdateSystemDescriptor> providerDescriptors = provider.GetUpdateSystems();
            for (int i = 0; i < providerDescriptors.Length; i++)
            {
                descriptors.Add(providerDescriptors[i]);
            }
        }
    }
}
