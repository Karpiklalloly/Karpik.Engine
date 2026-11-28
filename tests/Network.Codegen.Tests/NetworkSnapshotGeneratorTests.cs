using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Network.Codegen.Tests;

public sealed class NetworkSnapshotGeneratorTests
{
    [Fact]
    public void Shared_static_compilation_generates_typed_real_Transform2D_codec()
    {
        var result = GeneratorTestHarness.Run(
            new NetworkGenerator(),
            additionalReferences: GeneratorTestHarness.RealSnapshotReferences);

        result.AssertNoErrors();
        var source = result.SnapshotSource;
        Assert.Contains("namespace Karpik.Engine.Generated", source, StringComparison.Ordinal);
        Assert.Contains("public sealed class NetworkSnapshotRegistry", source, StringComparison.Ordinal);
        Assert.Contains("ReadOnlySpan<int> destroyedNetworkIds", source, StringComparison.Ordinal);
        Assert.Contains("writer.Put(component.Position.X);", source, StringComparison.Ordinal);
        Assert.Contains("writer.Put(component.Position.Y);", source, StringComparison.Ordinal);
        Assert.Contains("writer.Put(component.Rotation);", source, StringComparison.Ordinal);
        Assert.Contains("writer.Put(component.Scale.X);", source, StringComparison.Ordinal);
        Assert.Contains("writer.Put(component.Scale.Y);", source, StringComparison.Ordinal);
        Assert.Contains("reader.GetDouble()", source, StringComparison.Ordinal);
        Assert.Contains("reader.GetFloat()", source, StringComparison.Ordinal);

        var assembly = GeneratorTestHarness.EmitAndLoad(result);
        var registry = assembly.GetType("Karpik.Engine.Generated.NetworkSnapshotRegistry", throwOnError: true)!;
        Assert.Equal(0x168D_A12F_1F46_9220UL, registry.GetField("ComponentId0", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue());
        Assert.Equal(0x0F44_6ADB_208D_8727L, registry.GetField("ProtocolSchemaHash", BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue());
    }

    [Theory]
    [InlineData("Client")]
    [InlineData("Server")]
    public void Client_and_server_suppress_snapshot_generation(string side)
    {
        var result = GeneratorTestHarness.Run(
            new NetworkGenerator(),
            assemblyName: "Misleading.MyGame.Shared.Main",
            side: side,
            additionalReferences: GeneratorTestHarness.RealSnapshotReferences);

        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Rpc_dispatcher_uses_build_property_not_misleading_assembly_name()
    {
        var result = GeneratorTestHarness.Run(
            new RpcGenerator(),
            source: RpcSurface,
            assemblyName: "Misleading.MyGame.Client.Main",
            side: "Server");

        result.AssertNoErrors();
        Assert.Contains("ServerCommandDispatcher.g.cs", result.GeneratedSources.Keys);
        Assert.NotNull(result.Compilation.GetTypeByMetadataName("Karpik.Engine.Generated.CommandDispatcher"));
    }

    [Fact]
    public void Target_client_dispatcher_uses_build_property_not_misleading_assembly_name()
    {
        var result = GeneratorTestHarness.Run(
            new TargetClientRpcGenerator(),
            source: TargetRpcSurface,
            assemblyName: "Misleading.MyGame.Server.Main",
            side: "Client");

        result.AssertNoErrors();
        Assert.Contains("TargetClientRpcDispatcher.g.cs", result.GeneratedSources.Keys);
        Assert.NotNull(result.Compilation.GetTypeByMetadataName("Karpik.Engine.Generated.TargetClientRpcDispatcher"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Desktop")]
    public void Missing_or_invalid_side_does_not_guess_from_assembly_name(string side)
    {
        var result = GeneratorTestHarness.Run(
            new NetworkGenerator(),
            assemblyName: "EndsWithMyGame.Shared.Main",
            side: side,
            additionalReferences: GeneratorTestHarness.RealSnapshotReferences);

        Assert.DoesNotContain("NetworkSnapshotRegistry.g.cs", result.GeneratedSources.Keys);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "KNET001");
    }

    [Fact]
    public void Unsupported_managed_networked_field_reports_error_at_field()
    {
        const string source = """
            using Karpik.Engine.Shared.Network.Core;
            namespace TestGame;
            [NetworkedComponent]
            public struct BadComponent
            {
                [NetworkedField]
                public object Payload;
            }
            """;
        var result = GeneratorTestHarness.Run(
            new NetworkGenerator(),
            source,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Shared.Network.Core.NetworkedComponentAttribute>(),
                GeneratorTestHarness.AssemblyReference<DCFApixels.DragonECS.EcsWorld>(),
            ]);

        var diagnostic = Assert.Single(result.Diagnostics.Where(static diagnostic => diagnostic.Id == "KNET002"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Payload", diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void Managed_string_snapshot_field_reports_error_at_field()
    {
        const string source = """
            using DCFApixels.DragonECS;
            using Karpik.Engine.Shared.Network.Core;
            namespace TestGame;
            [NetworkedComponent]
            public struct BadComponent : IEcsComponent
            {
                [NetworkedField]
                public string Name;
            }
            """;
        var result = GeneratorTestHarness.Run(
            new NetworkGenerator(),
            source,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Shared.Network.Core.NetworkedComponentAttribute>(),
                GeneratorTestHarness.AssemblyReference<DCFApixels.DragonECS.EcsWorld>(),
            ]);

        var diagnostic = Assert.Single(result.Diagnostics.Where(static diagnostic => diagnostic.Id == "KNET002"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Name", diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void Managed_class_snapshot_component_reports_error_at_component()
    {
        const string source = """
            using DCFApixels.DragonECS;
            using Karpik.Engine.Shared.Network.Core;
            namespace TestGame;
            [NetworkedComponent]
            public sealed class BadComponent : IEcsComponent
            {
                [NetworkedField]
                public int Value;
            }
            """;
        var result = GeneratorTestHarness.Run(
            new NetworkGenerator(),
            source,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Shared.Network.Core.NetworkedComponentAttribute>(),
                GeneratorTestHarness.AssemblyReference<DCFApixels.DragonECS.EcsWorld>(),
            ]);

        var diagnostic = Assert.Single(result.Diagnostics.Where(static diagnostic => diagnostic.Id == "KNET004"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("BadComponent", diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void Snapshot_struct_without_ecs_component_contract_reports_error_at_component()
    {
        const string source = """
            using Karpik.Engine.Shared.Network.Core;
            namespace TestGame;
            [NetworkedComponent]
            public struct BadComponent
            {
                [NetworkedField]
                public int Value;
            }
            """;
        var result = GeneratorTestHarness.Run(
            new NetworkGenerator(),
            source,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Shared.Network.Core.NetworkedComponentAttribute>(),
                GeneratorTestHarness.AssemblyReference<DCFApixels.DragonECS.EcsWorld>(),
            ]);

        var diagnostic = Assert.Single(result.Diagnostics.Where(static diagnostic => diagnostic.Id == "KNET004"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("BadComponent", diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void Duplicate_component_metadata_name_reports_component_id_collision()
    {
        const string component = """
            using DCFApixels.DragonECS;
            using Karpik.Engine.Shared.Network.Core;
            namespace Collision;
            [NetworkedComponent]
            public struct Duplicate : IEcsComponent { [NetworkedField] public int Value; }
            """;
        var first = GeneratorTestHarness.CompileReference("First.Components", component);
        var second = GeneratorTestHarness.CompileReference("Second.Components", component);

        var result = GeneratorTestHarness.Run(
            new NetworkGenerator(),
            additionalReferences:
            [
                first,
                second,
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Shared.Network.Core.NetworkedComponentAttribute>(),
                GeneratorTestHarness.AssemblyReference<DCFApixels.DragonECS.EcsWorld>(),
            ]);

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "KNET003");
    }

    [Fact]
    public void Component_order_is_stable_when_reference_order_changes()
    {
        var alpha = GeneratorTestHarness.CompileReference("Alpha.Components", ComponentSource("Alpha", "One"));
        var omega = GeneratorTestHarness.CompileReference("Omega.Components", ComponentSource("Omega", "Two"));
        var shared = new MetadataReference[]
        {
            GeneratorTestHarness.AssemblyReference<Karpik.Engine.Shared.Network.Core.NetworkedComponentAttribute>(),
            GeneratorTestHarness.AssemblyReference<DCFApixels.DragonECS.EcsWorld>(),
            GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.ServiceRegistrationAttribute>(),
            GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
        };

        var forward = GeneratorTestHarness.Run(new NetworkGenerator(), additionalReferences: shared.Concat([alpha, omega]));
        var reverse = GeneratorTestHarness.Run(new NetworkGenerator(), additionalReferences: shared.Concat([omega, alpha]));

        forward.AssertNoErrors();
        reverse.AssertNoErrors();
        Assert.Equal(forward.SnapshotSource, reverse.SnapshotSource);
    }

    [Fact]
    public void Snapshot_hot_methods_have_no_dynamic_dispatch_or_transient_constructs()
    {
        var result = GeneratorTestHarness.Run(
            new NetworkGenerator(),
            additionalReferences: GeneratorTestHarness.RealSnapshotReferences);

        result.AssertNoErrors();
        var tree = CSharpSyntaxTree.ParseText(result.SnapshotSource, cancellationToken: TestContext.Current.CancellationToken);
        var root = tree.GetRoot(TestContext.Current.CancellationToken);
        var hotMethods = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(static method => method.Identifier.ValueText is "WriteSnapshot" or "ApplySnapshot")
            .ToArray();
        Assert.Equal(2, hotMethods.Length);
        var hotText = string.Join(Environment.NewLine, hotMethods.Select(static method => method.ToFullString()));
        Assert.DoesNotContain("object", hotText, StringComparison.Ordinal);
        Assert.DoesNotContain("Dictionary", hotText, StringComparison.Ordinal);
        Assert.DoesNotContain("IComponentSerializer", hotText, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Linq", result.SnapshotSource, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", result.SnapshotSource, StringComparison.Ordinal);
        Assert.Empty(hotMethods.SelectMany(static method => method.DescendantNodes().OfType<AnonymousFunctionExpressionSyntax>()));
        Assert.Empty(hotMethods.SelectMany(static method => method.DescendantNodes().OfType<ArrayCreationExpressionSyntax>()));
        Assert.Empty(hotMethods.SelectMany(static method => method.DescendantNodes().OfType<ImplicitArrayCreationExpressionSyntax>()));
    }

    [Fact]
    public void Generated_real_snapshot_round_trips_fractional_doubles_and_warmed_writes_allocate_zero_bytes()
    {
        var result = GeneratorTestHarness.Run(
            new NetworkGenerator(),
            source: SnapshotProbeSource,
            assemblyName: "AnyGame.Shared.SnapshotProbe",
            additionalReferences: GeneratorTestHarness.RealSnapshotReferences);
        result.AssertNoErrors();
        var assembly = GeneratorTestHarness.EmitAndLoad(result);
        var probe = assembly.GetType("SnapshotProbe", throwOnError: true)!;

        Assert.True((bool)probe.GetMethod("RoundTrip", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!);
        var allocated = (long)probe.GetMethod("MeasureWrites", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Generated_rpc_dispatch_stamps_deferred_ecs_event_before_application_consumes_it()
    {
        var result = GeneratorTestHarness.Run(new RpcGenerator(), RevisionRpcProbe,
            assemblyName: "AnyGame.Server.RevisionRpcProbe", side: "Server",
            additionalReferences: GeneratorTestHarness.RealSnapshotReferences.Concat([
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Shared.DragonECS.IEcsComponentEvent>(),
            ]));
        Assert.Contains("RevisionGameplayEvent.Enqueue", result.GeneratedSources["ServerCommandDispatcher.g.cs"], StringComparison.Ordinal);
        result.AssertNoErrors();
        var probe = GeneratorTestHarness.EmitAndLoad(result).GetType("RevisionRpcProbe", throwOnError: true)!;
        Assert.True((bool)probe.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!);
    }

    private static readonly string RevisionRpcProbe = """
        using System;
        using System.Drawing;
        using DCFApixels.DragonECS;
        using Karpik.Engine.Generated;
        using Karpik.Engine.Shared.Extensions;
        using Karpik.Engine.Shared.Network.Core;

        public struct ProbeCommand : IEventCommand
        {
            public int Source { get; set; }
            public int Target { get; set; }
            public int Value;
        }

        public static class RevisionRpcProbe
        {
            private sealed class Aspect : EcsAspect { public EcsPool<ProbeCommand> Commands = Inc; }
            public static bool Run()
            {
                var world = new EcsEventWorld();
                var rpc = new Rpc();
                rpc.Probe(new ProbeCommand { Value = 42 });
                rpc.Buffer.Rewind();
                if (rpc.Buffer.GetByte() != (byte)PacketType.Command) return false;
                new CommandDispatcher(world).Dispatch(5, rpc.Buffer);
                var pending = world.Where(out Aspect aspect);
                var stamps = world.GetPool<RevisionGameplayStamp>();
                if (pending.Count != 1 || !RevisionGameplayEvent.IsCurrent(stamps, pending[0], 31) ||
                    RevisionGameplayEvent.IsCurrent(stamps, pending[0], 32)) return false;
                return aspect.Commands.Get(pending[0]).Value == 42;
            }
            private sealed class Rpc : IRpc
            {
                public BufferCodec Buffer = new(128) { GameplayRevision = 31 };
                public IWriter GetWriter() => Buffer;
                public void Send(DeliveryMethod method) { }
            }
        }
        """ + SnapshotProbeSource[SnapshotProbeSource.IndexOf("public sealed class BufferCodec", StringComparison.Ordinal)..];

    [Fact]
    public void Generated_revision_baseline_validates_before_reconciling_owned_incarnations_and_fields()
    {
        var result = GeneratorTestHarness.Run(new NetworkGenerator(), RevisionBaselineProbe,
            assemblyName: "AnyGame.Shared.RevisionBaselineProbe",
            additionalReferences: GeneratorTestHarness.RealSnapshotReferences);
        Assert.Contains("IRevisionReplicaProjection", result.SnapshotSource, StringComparison.Ordinal);
        result.AssertNoErrors();
        var probe = GeneratorTestHarness.EmitAndLoad(result).GetType("RevisionBaselineProbe", throwOnError: true)!;
        var failure = probe.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
        Assert.Null(failure);
    }

    private const string RevisionBaselineProbe = """
        using System;
        using DCFApixels.DragonECS;
        using Karpik.Engine.Generated;
        using Karpik.Engine.Shared.Network.Core;

        [NetworkedComponent]
        public struct Position : IEcsComponent
        {
            [NetworkedField] public int X;
            public int LocalValue;
        }
        [NetworkedComponent]
        public struct Removed : IEcsComponent { [NetworkedField] public int Value; }
        public struct ModField : IEcsComponent { public int Projected; public int Private; }
        public struct ClientOnly : IEcsComponent { public int Sentinel; }

        public sealed class TestModProjection(long hash) : IRevisionReplicaProjection
        {
            public long SchemaHash => hash;
            public void Write(EcsWorld world, int entity, IWriter writer)
            {
                var pool = world.GetPool<ModField>();
                writer.Put(pool.Has(entity));
                if (pool.Has(entity)) writer.Put(pool.Get(entity).Projected);
            }
            public void Validate(IReader reader) { if (reader.GetBool()) _ = reader.GetInt(); }
            public void Apply(EcsWorld world, int entity, IReader reader)
            {
                var pool = world.GetPool<ModField>();
                if (reader.GetBool()) pool.TryAddOrGet(entity).Projected = reader.GetInt();
                else pool.TryDel(entity);
            }
        }

        public static class RevisionBaselineProbe
        {
            public static string Run()
            {
                var s = new EcsWorld();
                var c = new EcsWorld();
                var projection = new NetworkSnapshotRegistry();
                // Position and Removed each use 5 bytes; referenced Transform2D uses 1 + 16 + 4 + 8.
                if (((IRevisionReplicaProjection)projection).MaximumRowBytes != 39) return "Generated bound omits presence bytes or fields.";
                var sender = new RevisionReplicaBaseline(s, 4, projection, new TestModProjection(808));
                var receiver = new RevisionReplicaBaseline(c, 4, projection, new TestModProjection(808));
                var otherSchema = new RevisionReplicaBaseline(c, 4, projection, new TestModProjection(809));
                int a = s.NewEntity(), b = s.NewEntity();
                s.GetPool<Position>().Add(a).X = 101;
                s.GetPool<Position>().Add(b).X = 202;
                s.GetPool<ModField>().Add(a).Projected = 301;
                s.GetPool<ModField>().Get(a).Private = -100;

                int survivor = Existing(c, 7, 1, 4), oldIncarnation = Existing(c, 8, 1, 4), absent = Existing(c, 9, 1, 4);
                int differentStream = Existing(c, 7, 99, 5), local = c.NewEntity();
                var oldHandle = c.GetEntityLong(oldIncarnation);
                c.GetPool<NetworkId>().Add(local).Id = 7; // network-looking data is not ownership
                c.GetPool<ClientOnly>().Add(local).Sentinel = 42;
                c.GetPool<Position>().Add(survivor).LocalValue = 54;
                c.GetPool<Removed>().Add(survivor).Value = 63;
                c.GetPool<ModField>().Add(survivor).Private = 88;

                if (!sender.TryCapture([new(a, 7, 1), new(b, 8, 2)], 32)) return "capture";
                var bytes = sender.Encoded.ToArray();
                if (sender.TryCapture([new(a, 7, 1), new(b, 7, 2)], 32)) return "duplicate numeric identity";
                if (receiver.TryApply(bytes.AsMemory(0, bytes.Length - 1), 32) ||
                    receiver.TryApply(new byte[RevisionReplicaBaseline.MaxBytes + 1], 32) ||
                    receiver.TryApply(bytes, 31) || otherSchema.TryApply(bytes, 32)) return "invalid baseline accepted";
                if (!c.GetPool<Removed>().Has(survivor) || !oldHandle.IsAlive || !c.GetEntityLong(absent).IsAlive ||
                    c.GetPool<Position>().Get(survivor).X != 0 || c.GetPool<ModField>().Get(survivor).Projected != 0)
                    return "invalid baseline published partial changes";
                if (!receiver.TryApply(bytes, 32)) return "valid baseline rejected";
                if (!receiver.TryFind(7, 1, out var same) || same != survivor || c.GetPool<Removed>().Has(survivor) ||
                    c.GetPool<Position>().Get(survivor).LocalValue != 54 || c.GetPool<Position>().Get(survivor).X != 101 ||
                    c.GetPool<ModField>().Get(survivor).Private != 88 || c.GetPool<ModField>().Get(survivor).Projected != 301)
                    return "survivor or component absence";
                if (receiver.TryFind(8, 1, out _) || !receiver.TryFind(8, 2, out var replacement) ||
                    oldHandle.IsAlive || c.GetPool<Position>().Get(replacement).X != 202 || receiver.TryFind(9, 1, out _))
                    return "replacement/absence";
                if (!c.GetEntityLong(differentStream).IsAlive || c.GetPool<ClientOnly>().Get(local).Sentinel != 42)
                    return "foreign or local-only entity changed";
                return null;
            }

            private static int Existing(EcsWorld world, int id, long incarnation, long stream)
            {
                var entity = world.NewEntity();
                world.GetPool<NetworkReplica>().Add(entity) = new NetworkReplica { Stream = stream, Id = id, Incarnation = incarnation };
                return entity;
            }
        }
        """;

    private static string ComponentSource(string ns, string name) => $$"""
        using DCFApixels.DragonECS;
        using Karpik.Engine.Shared.Network.Core;
        namespace {{ns}};
        [NetworkedComponent]
        public struct {{name}} : IEcsComponent { [NetworkedField] public int Value; }
        """;

    private const string RpcSurface = """
        using System.Collections.Generic;
        namespace Karpik.Engine.Shared.Network.Core
        {
            public interface IEventCommand { }
            public interface IStateCommand { }
            public interface IWriter { void Reset(); void Put(byte value); void Put(ushort value); void Put(int value); }
            public interface IReader { ushort GetUShort(); int GetInt(); }
            public interface IRpc { IWriter GetWriter(); void Send(DeliveryMethod method); }
            public enum PacketType : byte { Command }
            public enum DeliveryMethod { ReliableOrdered }
            public struct TestCommand : IEventCommand { public int Value; }
        }
        namespace Karpik.Engine.Shared.DragonECS
        {
            public interface IEcsComponentRequest { int Target { get; set; } IEnumerable<int> Sources { get; set; } }
        }
        namespace Karpik.Engine.Shared { }
        namespace Karpik.Engine.Core { }
        namespace DCFApixels.DragonECS
        {
            public sealed class EcsEventWorld { public void SendEvent<T>(T value) { } }
        }
        """;

    private const string TargetRpcSurface = """
        namespace Karpik.Engine.Shared.Network.Core
        {
            public interface ITargetRpcCommand { }
            public interface IClientRpcCommand { }
            public interface IWriter { void Reset(); void Put(byte value); void Put(ushort value); void Put(int value); }
            public interface IReader { ushort GetUShort(); int GetInt(); }
            public interface IPeer { void Send(IWriter writer, DeliveryMethod method); }
            public interface INetworkManager { void SendToAll(IWriter writer, DeliveryMethod method); }
            public interface ITargetRpcSender { IWriter GetWriter(); INetworkManager GetManager(); }
            public enum PacketType : byte { Command }
            public enum DeliveryMethod { ReliableOrdered }
            public struct TestTargetRpc : ITargetRpcCommand { public int Value; }
        }
        namespace Karpik.Engine.Shared { }
        namespace Karpik.Engine.Core { }
        namespace Karpik.Engine.Shared.DragonECS { }
        namespace DCFApixels.DragonECS
        {
            public sealed class EcsEventWorld { public void SendEvent<T>(T value) { } }
        }
        """;

    private const string SnapshotProbeSource = """
        using System;
        using System.Drawing;
        using DCFApixels.DragonECS;
        using Karpik.Engine.Generated;
        using Karpik.Engine.Shared.Network.Core;
        using Karpik.Engine.Shared.Spatial2D;
        using OpenTK.Mathematics;

        public static class SnapshotProbe
        {
            private sealed class Aspect : EcsAspect { public EcsPool<NetworkId> NetworkId = Inc; }

            public static bool RoundTrip()
            {
                var source = CreateWorld();
                var destination = new EcsWorld();
                var codec = new BufferCodec(4096);
                var registry = new NetworkSnapshotRegistry();
                registry.WriteSnapshot(source, codec, ReadOnlySpan<int>.Empty);
                codec.Rewind();
                registry.ApplySnapshot(destination, codec);
                var entities = destination.Where(out Aspect aspect);
                if (entities.Count != 1) return false;
                ref var value = ref destination.GetPool<Transform2D>().Get(entities[0]);
                return value.Position.X == 123.125d && value.Position.Y == -456.875d &&
                       value.Rotation == 0.375f && value.Scale.X == 1.25f && value.Scale.Y == 2.5f;
            }

            public static long MeasureWrites()
            {
                var world = CreateWorld();
                var codec = new BufferCodec(4096);
                var registry = new NetworkSnapshotRegistry();
                registry.WriteSnapshot(world, codec, ReadOnlySpan<int>.Empty);
                codec.Reset();
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 1000; i++)
                {
                    registry.WriteSnapshot(world, codec, ReadOnlySpan<int>.Empty);
                    codec.Reset();
                }
                return GC.GetAllocatedBytesForCurrentThread() - before;
            }

            private static EcsWorld CreateWorld()
            {
                var world = new EcsWorld();
                var entity = world.NewEntity();
                world.GetPool<NetworkId>().Add(entity).Id = 42;
                ref var value = ref world.GetPool<Transform2D>().Add(entity);
                value.Position = new Vector2d(123.125d, -456.875d);
                value.Rotation = 0.375f;
                value.Scale = new Vector2(1.25f, 2.5f);
                return world;
            }
        }

        public sealed class BufferCodec : IWriter, IReader
        {
            private readonly byte[] _buffer;
            private int _position;
            public BufferCodec(int capacity) => _buffer = new byte[capacity];
            public long GameplayRevision { get; set; }
            public int AvailableBytes => _buffer.Length - _position;
            public void Reset() => _position = 0;
            public void Rewind() => _position = 0;
            public void Recycle() { }
            public void Put(float value) => Put(BitConverter.SingleToInt32Bits(value));
            public void Put(int value) { _buffer[_position++] = (byte)value; _buffer[_position++] = (byte)(value >> 8); _buffer[_position++] = (byte)(value >> 16); _buffer[_position++] = (byte)(value >> 24); }
            public void Put(long value) { Put((int)value); Put((int)(value >> 32)); }
            public void Put(bool value) => Put((byte)(value ? 1 : 0));
            public void Put(string value) => throw new NotSupportedException();
            public void Put(byte value) => _buffer[_position++] = value;
            public void Put(ushort value) { _buffer[_position++] = (byte)value; _buffer[_position++] = (byte)(value >> 8); }
            public void Put(double value) => Put(BitConverter.DoubleToInt64Bits(value));
            public void Put(Color color) => Put(color.ToArgb());
            public float GetFloat() => BitConverter.Int32BitsToSingle(GetInt());
            public byte GetByte() => _buffer[_position++];
            public ushort GetUShort() => (ushort)(GetByte() | (GetByte() << 8));
            public int GetInt() => GetByte() | (GetByte() << 8) | (GetByte() << 16) | (GetByte() << 24);
            public long GetLong() => (uint)GetInt() | ((long)GetInt() << 32);
            public double GetDouble() => BitConverter.Int64BitsToDouble(GetLong());
            public bool GetBool() => GetByte() != 0;
            public string GetString() => throw new NotSupportedException();
            public Color GetColor() => Color.FromArgb(GetInt());
        }
        """;
}
