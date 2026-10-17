using System.Reflection;
using Autofac;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;
using Xunit;

namespace Network.Codegen.Tests;

public sealed class NetworkRuntimeSchemaWiringTests
{
    [Fact]
    public void Generated_nonzero_schema_is_exported_and_actual_client_server_startup_configure_it_before_start()
    {
        var result = GeneratorTestHarness.Run(
            new NetworkGenerator(),
            assemblyName: "AnyGame.Shared.RuntimeSchemaWiring",
            additionalReferences: GeneratorTestHarness.RealSnapshotReferences);
        result.AssertNoErrors();
        var generatedAssembly = GeneratorTestHarness.EmitAndLoad(result);
        var registryType = generatedAssembly.GetType(
            "Karpik.Engine.Generated.NetworkSnapshotRegistry",
            throwOnError: true)!;
        var schemaInterface = typeof(INetworkManager).Assembly.GetType(
            "Karpik.Engine.Shared.Network.Core.INetworkProtocolSchema",
            throwOnError: false);

        Assert.NotNull(schemaInterface);
        Assert.True(schemaInterface.IsAssignableFrom(registryType));
        var schema = Activator.CreateInstance(registryType)!;
        var schemaHash = (long)schemaInterface.GetProperty("ProtocolSchemaHash")!.GetValue(schema)!;
        Assert.NotEqual(0, schemaHash);
        Assert.Contains(
            registryType.GetCustomAttributesData(),
            attribute => attribute.AttributeType.FullName == "System.Composition.ExportAttribute" &&
                         attribute.ConstructorArguments.Any(argument => argument.Value as Type == schemaInterface));
        Assert.Contains(
            registryType.GetCustomAttributesData(),
            attribute => attribute.AttributeType.FullName == "Karpik.Engine.Core.ServiceRegistrationAttribute");

        AssertStartupWiring(
            "Network.Client.LiteNetLib",
            "Karpik.Engine.Client.Network.LiteNetLib.Systems.InitNetworkClientSystem",
            registryType,
            schemaInterface,
            schemaHash,
            ["Configure", "Start", "Connect"]);
        AssertStartupWiring(
            "Network.Server.LiteNetLib",
            "Network.Server.LiteNetLib.Systems.InitNetworkClientSystem",
            registryType,
            schemaInterface,
            schemaHash,
            ["Configure", "Start"]);
    }

    [Fact]
    public void Unconfigured_or_zero_schema_cannot_start_a_network_manager()
    {
        using var unconfigured = new Karpik.Engine.Shared.Network.LiteNetLib.LiteNetLibNetworkManager();
        using var zero = new Karpik.Engine.Shared.Network.LiteNetLib.LiteNetLibNetworkManager();

        Assert.Throws<InvalidOperationException>(() => unconfigured.Start(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => zero.ConfigureProtocolSchema(0));
    }

    private static void AssertStartupWiring(
        string assemblyName,
        string typeName,
        Type registryType,
        Type schemaInterface,
        long expectedSchemaHash,
        string[] expectedCalls)
    {
        var manager = new RecordingNetworkManager();
        var startupType = Assembly.Load(assemblyName).GetType(typeName, throwOnError: true)!;
        var runnerAssembly = Assembly.Load("Karpik.Engine.Core.Runner");
        var registrar = runnerAssembly.GetType(
            "Karpik.Engine.Core.Runner.AttributedServiceRegistrar",
            throwOnError: true)!;
        var builder = new ContainerBuilder();
        registrar.GetMethod("Register")!.Invoke(
            null,
            [builder, new[] { registryType }, ModuleScope.Simulation]);
        builder.RegisterInstance(manager).As<INetworkManager>();
        builder.RegisterInstance(new NetworkConfig()).AsSelf();
        builder.RegisterType(startupType).AsSelf();
        using var container = builder.Build();
        var schema = container.Resolve(schemaInterface);
        var registry = container.Resolve(registryType);
        var startup = container.Resolve(startupType);

        startupType.GetMethod("Init")!.Invoke(startup, null);

        Assert.Same(registry, schema);
        Assert.Equal(expectedSchemaHash, (long)schemaInterface.GetProperty("ProtocolSchemaHash")!.GetValue(schema)!);
        Assert.Equal(expectedSchemaHash, manager.SchemaHash);
        Assert.Equal(expectedCalls, manager.Calls);
    }

    private sealed class RecordingNetworkManager : INetworkManager
    {
        public event INetworkManager.NetworkEventHandler? NetworkReceiveEvent { add { } remove { } }
        public event INetworkManager.PeerConnectionEventHandler? PeerConnectedEvent { add { } remove { } }
        public event INetworkManager.PeerDisconnectionEventHandler? PeerDisconnectedEvent { add { } remove { } }
        public event INetworkManager.ConnectionRequestEventHandler? ConnectionRequestEvent { add { } remove { } }

        public IPeer? FirstPeer => null;
        public long SchemaHash { get; private set; }
        public List<string> Calls { get; } = [];

        public int GetFreePort() => 0;
        public void ConfigureProtocolSchema(long schemaHash)
        {
            SchemaHash = schemaHash;
            Calls.Add("Configure");
        }
        public void Start(int port) => Calls.Add("Start");
        public void Connect(string address, int port, string key) => Calls.Add("Connect");
        public void PollEvents() { }
        public void Stop() { }
        public void SendToAll(IWriter writer, DeliveryMethod deliveryMethod) { }
        public IWriter CreateWriter() => throw new NotSupportedException();
        public void Dispose() { }
    }
}
