using DCFApixels.DragonECS;
using Karpik.Engine.Core.Codegen;
using Microsoft.CodeAnalysis;
using System.Reflection;
using Autofac;
using Autofac.Builder;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Shared.ECS;
using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Lua;
using Karpik.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Karpik.Engine.Core.Generator.Tests;

public sealed class ClrComponentBindingGeneratorTests
{
    private static IIncrementalGenerator Generator()
    {
        Type? type = typeof(RuntimeCompositionGenerator).Assembly.GetType("Karpik.Engine.Core.Codegen.ClrComponentBindingGenerator");
        Assert.NotNull(type);
        return (IIncrementalGenerator)Activator.CreateInstance(type)!;
    }

    [Fact]
    public void ClrComponentBinding_GeneratesRefFloatAccess()
    {
        var result = GeneratorTestHarness.Run(Generator(), source: Source("public float X, Y;"), additionalReferences: References);
        result.AssertNoErrors();
        var assembly = GeneratorTestHarness.EmitAndLoad(result);
        var verify = assembly.GetType("BindingFixture.Check")!.GetMethod("Verify")!;
        Assert.Equal(12.5f, verify.Invoke(null, null));
    }

    [Fact]
    public void ClrComponentBinding_ReadonlyFieldCannotWrite()
    {
        var result = GeneratorTestHarness.Run(Generator(), source: Source("public readonly float X; public float Y;"), additionalReferences: References);
        result.AssertNoErrors();
        var assembly = GeneratorTestHarness.EmitAndLoad(result);
        Assert.Equal(true, assembly.GetType("BindingFixture.Check")!.GetMethod("RejectReadonly")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("public long X; public float Y;")]
    [InlineData("public E X; public float Y; public enum E { Value }")]
    [InlineData("public Nested X; public float Y; public struct Nested { public float Value; }")]
    [InlineData("public unsafe fixed float X[2]; public float Y;")]
    public void ClrComponentBinding_RejectsUnsupportedPublicField(string fields)
    {
        var result = GeneratorTestHarness.Run(Generator(), source: Source(fields), additionalReferences: References);
        Assert.NotEmpty(result.DiagnosticsById("KEB001"));
    }

    [Fact]
    public void ClrComponentBinding_GeneratesTypedScalarAccess()
    {
        const string fields = "public bool B; public double D; public int I; public uint U; public float X, Y; public readonly bool RB; public readonly double RD; public readonly float RF; public readonly int RI; public readonly uint RU;";
        string source = Source(fields) + """

            public static class TypedCheck
            {
                public static bool Verify()
                {
                    var binding = new Karpik.Generated.Bindings_AnyGame_002EClient_002EHost.GeneratedClrComponentBindingProvider().GetBindings().Span[0];
                    var world = new EcsDefaultWorld();
                    try
                    {
                        var pool = binding.Bind(world); int entity = world.NewEntity(); pool.Add(entity);
                        foreach (var field in pool.Fields.Span)
                        {
                            if (field.CanWrite) Write(field, entity);
                            else
                            {
                                try { Write(field, entity); return false; }
                                catch (System.InvalidOperationException) { }
                                bool zero = field.Kind switch {
                                    SchemaScalarKind.Bool => !field.Bool.Read(entity),
                                    SchemaScalarKind.I32 => field.I32.Read(entity) == 0,
                                    SchemaScalarKind.U32 => field.U32.Read(entity) == 0,
                                    SchemaScalarKind.F32 => field.F32.Read(entity) == 0,
                                    SchemaScalarKind.F64 => field.F64.Read(entity) == 0,
                                    _ => false
                                };
                                if (!zero) return false;
                            }
                        }
                        ref var p = ref world.GetPool<Position>().Get(entity);
                        return p.B && p.I == int.MinValue && p.U == uint.MaxValue && p.D == 16777217d && p.X == 1.25f && p.Y == 1.25f;
                    }
                    finally { world.Destroy(); }
                }
                private static void Write(ComponentFieldDescriptor field, int entity)
                {
                    switch (field.Kind) {
                        case SchemaScalarKind.Bool: field.Bool.Write(entity, true); break;
                        case SchemaScalarKind.I32: field.I32.Write(entity, int.MinValue); break;
                        case SchemaScalarKind.U32: field.U32.Write(entity, uint.MaxValue); break;
                        case SchemaScalarKind.F32: field.F32.Write(entity, 1.25f); break;
                        case SchemaScalarKind.F64: field.F64.Write(entity, 16777217d); break;
                    }
                }
            }
            """;
        var result = GeneratorTestHarness.Run(Generator(), source, additionalReferences: References);
        result.AssertNoErrors();
        var assembly = GeneratorTestHarness.EmitAndLoad(result);
        Assert.Equal(true, assembly.GetType("BindingFixture.TypedCheck")!.GetMethod("Verify")!.Invoke(null, null));
    }

    [Fact]
    public void ClrComponentBinding_ExportsPublicInstanceFieldsOnly()
    {
        var result = GeneratorTestHarness.Run(Generator(), source: Source("public float X, Y; private int Hidden; public static int Static; public int Property => 1;"), additionalReferences: References);
        result.AssertNoErrors();
        var assembly = GeneratorTestHarness.EmitAndLoad(result);
        Assert.Equal(new[] { "X", "Y" }, (string[])assembly.GetType("BindingFixture.Check")!.GetMethod("FieldNames")!.Invoke(null, null)!);
    }

    [Fact]
    public void ClrComponentBinding_GenericForwardingDiagnostic()
    {
        var result = GeneratorTestHarness.Run(Generator(), source: """
            using DCFApixels.DragonECS;
            using Karpik.Engine.Shared.Modding.Bindings;
            public static class Registration {
                public static void Forward<T>(ClrComponentBindings registry) where T : unmanaged, IEcsComponent => registry.Register<T>("value");
            }
            """, additionalReferences: References);
        Assert.NotEmpty(result.DiagnosticsById("KEB001"));
    }

    [Fact]
    public void ClrComponentBinding_ReferencedAssemblyProvider()
    {
        var module = GeneratorTestHarness.Run(Generator(), Source("public float X, Y;")
            .Replace("Bindings_AnyGame_002EClient_002EHost", "Bindings_External_002EComponents"), assemblyName: "External.Components", additionalReferences: References);
        module.AssertNoErrors();
        using var stream = new MemoryStream();
        Assert.True(module.Compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success);
        var dependency = new CompiledModule(MetadataReference.CreateFromImage(stream.ToArray()), stream.ToArray());
        var host = GeneratorTestHarness.Run(new RuntimeCompositionGenerator(), additionalReferences: AllBindingReferences().Append(dependency.Reference));
        host.AssertNoErrors();
        Assert.Contains("Karpik.Generated.Bindings_External_002EComponents.GeneratedClrComponentBindingProvider", host.CompositionSource);
        var assembly = GeneratorTestHarness.EmitAndLoad(module);
        Assert.Equal(12.5f, assembly.GetType("BindingFixture.Check")!.GetMethod("Verify")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("Client")]
    [InlineData("Server")]
    public void ClrComponentBinding_OwnStaticHostProvider(string side)
    {
        var result = GeneratorTestHarness.Run(new BindingsAndComposition(), Source("public float X, Y;"), side: side,
            additionalReferences: AllBindingReferences());
        result.AssertNoErrors();
        Assert.Contains("Karpik.Generated.Bindings_AnyGame_002EClient_002EHost.GeneratedClrComponentBindingProvider", result.CompositionSource);
        GeneratorTestHarness.EmitAndLoad(result);
    }

    private sealed class BindingsAndComposition : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            Generator().Initialize(context);
            new RuntimeCompositionGenerator().Initialize(context);
        }
    }

    private static IEnumerable<MetadataReference> AllBindingReferences()
    {
        var pending = new Queue<Assembly>();
        pending.Enqueue(typeof(Karpik.Engine.Shared.ECS.ComponentTemplateBase).Assembly);
        pending.Enqueue(typeof(System.Composition.ExportAttribute).Assembly);
        pending.Enqueue(typeof(LuaScriptRuntime).Assembly);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var assembly))
        {
            if (!seen.Add(assembly.FullName!)) continue;
            if ((assembly.GetName().Name!.StartsWith("System.", StringComparison.Ordinal) && assembly != typeof(System.Composition.ExportAttribute).Assembly) || assembly.GetName().Name is "System" or "netstandard" or "mscorlib") continue;
            yield return MetadataReference.CreateFromFile(assembly.Location);
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies()) pending.Enqueue(Assembly.Load(reference));
        }
    }

    private static MetadataReference[] References =>
    [
        GeneratorTestHarness.AssemblyReference<IEcsComponent>(),
        GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
        GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
        GeneratorTestHarness.AssemblyReference<Karpik.Engine.Shared.ECS.ComponentTemplateBase>(),
        GeneratorTestHarness.AssemblyReference<ClrComponentBindings>()
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClrComponentBinding_ComposedScopesResolveLuaPreparation(bool useStatic)
    {
        var result = GeneratorTestHarness.Run(new BindingsAndComposition(), Source("public float X, Y;"), side: "Server", additionalReferences: AllBindingReferences());
        result.AssertNoErrors();
        var assembly = GeneratorTestHarness.EmitAndLoad(result);
        var registrations = new AutofacStaticServiceRegistry();
        var composition = (IStaticRuntimeComposition)Activator.CreateInstance(assembly.GetTypes().Single(type => typeof(IStaticRuntimeComposition).IsAssignableFrom(type)))!;
        composition.RegisterServices(registrations);
        Type provider = assembly.GetTypes().Single(type => typeof(IClrComponentBindingProvider).IsAssignableFrom(type));
        Type[] types = [typeof(ClrComponentBindings), typeof(ComponentSchemaRegistry), typeof(ComponentQueryPreparer),
            typeof(ComponentCallbackScope), typeof(LuaComponentRegistry), typeof(ScriptSystemOptions),
            typeof(LuaSimulationPreparation), typeof(ModsLifecycleManager), typeof(LuaScriptLoader), provider];
        var builder = new ContainerBuilder();
        Register(builder, ModuleScope.Engine);
        builder.RegisterType<AutofacServiceResolver>().As<IServiceResolver>().InstancePerLifetimeScope();
        builder.RegisterInstance(new PhysicalFileSystem()).As<IFileSystem>();
        builder.RegisterInstance(NullLogger<IScriptLoader>.Instance).As<ILogger<IScriptLoader>>();
        builder.RegisterInstance(new Application(Side.Server)).AsSelf();
        using var engine = builder.Build(ContainerBuildOptions.IgnoreStartableComponents);
        using var modSet = engine.BeginLifetimeScope(scope =>
        {
            Register(scope, ModuleScope.ModSet);
            scope.RegisterInstance(new EmptyCatalog()).As<IModsRegistry>();
        });
        using var first = modSet.BeginLifetimeScope(RegisterSimulation);
        using var second = modSet.BeginLifetimeScope(RegisterSimulation);
        Assert.IsType<LuaSimulationPreparation>(first.Resolve<ISimulationPreparation>());
        Assert.IsType<LuaSimulationPreparation>(second.Resolve<ISimulationPreparation>());
        Assert.Same(engine.Resolve<ClrComponentBindings>(), first.Resolve<LuaComponentRegistry>().Bindings);
        Assert.Same(first.Resolve<ComponentSchemaRegistry>(), second.Resolve<ComponentSchemaRegistry>());
        Assert.NotSame(first.Resolve<ComponentQueryPreparer>(), second.Resolve<ComponentQueryPreparer>());
        Assert.NotSame(first.Resolve<ComponentCallbackScope>(), second.Resolve<ComponentCallbackScope>());
        Assert.NotSame(first.Resolve<EcsDefaultWorld>(), second.Resolve<EcsDefaultWorld>());
        Assert.Contains(engine.Resolve<IEnumerable<IClrComponentBindingProvider>>(), item => item.GetBindings().Span[0].ComponentType.FullName == "BindingFixture.Position");

        void Register(ContainerBuilder scope, ModuleScope kind)
        {
            if (useStatic) registrations.Apply(scope, kind);
            else AttributedServiceRegistrar.Register(scope, types, kind);
        }
        void RegisterSimulation(ContainerBuilder scope)
        {
            Register(scope, ModuleScope.Simulation);
            new EcsModuleInstaller().OnRegisterServices(scope);
            scope.RegisterInstance(new SystemRegistry()).As<ISystemRuntimeRegistry>();
            scope.RegisterType<Time>().AsSelf().SingleInstance();
        }
    }

    private sealed class EmptyCatalog : IModsRegistry
    {
        public JobHandle Scan(ExecutionSide side, CancellationToken cancellationToken = default) => JobHandle.Completed;
        public IEnumerable<ModDefinition> GetMods() => [];
    }

    [Fact]
    public void ClrComponentBinding_AssemblyNamesDoNotCollideAfterSanitizing()
    {
        const string source = """
            using DCFApixels.DragonECS;
            using Karpik.Engine.Shared.Modding.Bindings;
            public struct Position : IEcsComponent { public float X; }
            public static class Registration { public static void Apply(ClrComponentBindings r) => r.Register<Position>("position"); }
            """;
        var first = GeneratorTestHarness.Run(Generator(), source, assemblyName: "A.B", additionalReferences: References);
        var second = GeneratorTestHarness.Run(Generator(), source, assemblyName: "A_B", additionalReferences: References);
        first.AssertNoErrors(); second.AssertNoErrors();
        var names = new[] { first, second }.Select(result => result.Compilation.GetSymbolsWithName("GeneratedClrComponentBindingProvider")
            .OfType<INamedTypeSymbol>().Single().ToDisplayString()).ToArray();
        Assert.NotEqual(names[0], names[1]);
    }

    private static string Source(string fields) => $$"""
        using DCFApixels.DragonECS;
        using Karpik.Engine.Shared.Modding.Bindings;
        namespace BindingFixture;
        public struct Position : IEcsComponent { {{fields}} }
        public static class Check
        {
            public static void Register(ClrComponentBindings bindings) => bindings.Register<Position>("game:Position");
            public static float Verify()
            {
                var bindings = new ClrComponentBindings(new IClrComponentBindingProvider[] { new Karpik.Generated.Bindings_AnyGame_002EClient_002EHost.GeneratedClrComponentBindingProvider() });
                Register(bindings);
                var world = new EcsDefaultWorld();
                try
                {
                    var pool = bindings.ResolveBinding("game:Position").Bind(world);
                    int entity = world.NewEntity();
                    pool.Add(entity);
                    var field = pool.Fields.Span[1].F32;
                    field.Write(entity, 12.5f);
                    return world.GetPool<Position>().Get(entity).Y;
                }
                finally { world.Destroy(); }
            }
            public static bool RejectReadonly()
            {
                var binding = new Karpik.Generated.Bindings_AnyGame_002EClient_002EHost.GeneratedClrComponentBindingProvider().GetBindings().Span[0];
                var world = new EcsDefaultWorld();
                try {
                    var pool = binding.Bind(world); int entity = world.NewEntity(); pool.Add(entity);
                    try { pool.Fields.Span[0].F32.Write(entity, 2); return false; }
                    catch (System.InvalidOperationException) { return !pool.Fields.Span[0].CanWrite && pool.Fields.Span[0].F32.Read(entity)==0; }
                } finally { world.Destroy(); }
            }
            public static string[] FieldNames()
            {
                var binding = new Karpik.Generated.Bindings_AnyGame_002EClient_002EHost.GeneratedClrComponentBindingProvider().GetBindings().Span[0];
                var world = new EcsDefaultWorld();
                try {
                    var fields = binding.Bind(world).Fields.Span;
                    var names = new string[fields.Length]; for(int i=0;i<fields.Length;i++) names[i]=fields[i].Name;
                    return names;
                } finally { world.Destroy(); }
            }
        }
        """;
}
