using StaticAnalyzer.ECS;
using Xunit;

namespace StaticAnalyzer.Tests;

public sealed class EcsUpdateSchedulerAnalyzerTests
{
    [Fact]
    public async Task ReadonlyPoolAccessWithReadSummary_IsAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using DCFApixels.DragonECS;
            using Karpik.Engine.Core;
            using Karpik.Engine.Shared.ECS.Scheduling;

            [Reads<Position>]
            public sealed class MovementSystem : ISystemUpdate
            {
                private EcsReadonlyPool<Position> _positions;

                public void Update()
                {
                    _positions.Get(1);
                }
            }

            public struct Position : IEcsComponent
            {
                public int X;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task MutablePoolAccessContradictsReadOnlySummary()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using DCFApixels.DragonECS;
            using Karpik.Engine.Core;
            using Karpik.Engine.Shared.ECS.Scheduling;

            [Reads<Position>]
            public sealed class MovementSystem : ISystemUpdate
            {
                private EcsPool<Position> _positions;

                public void Update()
                {
                    _positions.Get(1);
                }
            }

            public struct Position : IEcsComponent
            {
                public int X;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.EcsAccessSummaryContradiction, diagnostic.Id);
    }

    [Fact]
    public async Task SourceHelperWithoutSummary_IsTraversed()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using DCFApixels.DragonECS;
            using Karpik.Engine.Core;
            using Karpik.Engine.Shared.ECS.Scheduling;

            [Reads<Position>]
            public sealed class MovementSystem : ISystemUpdate
            {
                private EcsReadonlyPool<Position> _positions;

                public void Update()
                {
                    ReadPosition();
                }

                private void ReadPosition()
                {
                    _positions.Get(1);
                }
            }

            public struct Position : IEcsComponent
            {
                public int X;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task SourceHelperSummaryContradictingBody_IsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using DCFApixels.DragonECS;
            using Karpik.Engine.Core;
            using Karpik.Engine.Shared.ECS.Scheduling;

            public sealed class MovementSystem : ISystemUpdate
            {
                private EcsPool<Position> _positions;

                public void Update()
                {
                    ReadPosition();
                }

                [Reads<Position>]
                private void ReadPosition()
                {
                    _positions.Get(1);
                }
            }

            public struct Position : IEcsComponent
            {
                public int X;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.EcsAccessSummaryContradiction, diagnostic.Id);
    }

    [Fact]
    public async Task DelegateInvocationInsideUpdate_IsOpaque()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using System;
            using DCFApixels.DragonECS;
            using Karpik.Engine.Core;

            public sealed class MovementSystem : ISystemUpdate
            {
                public void Update()
                {
                    Action action = Touch;
                    action();
                }

                private void Touch()
                {
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.EcsOpaqueUpdateAccess, diagnostic.Id);
    }

    [Fact]
    public async Task SequentialSystem_AllowsOpaqueUpdateBody()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using System;
            using Karpik.Engine.Core;
            using Karpik.Engine.Shared.ECS.Scheduling;

            [SequentialSystem]
            public sealed class MovementSystem : ISystemUpdate
            {
                public void Update()
                {
                    Action action = Touch;
                    action();
                }

                private void Touch()
                {
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ExternalMethodWithoutSummary_IsOpaque()
    {
        var externalSource =
            """
            public static class ExternalMovementHelpers
            {
                public static void Move()
                {
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(
            """
            using Karpik.Engine.Core;

            public sealed class MovementSystem : ISystemUpdate
            {
                public void Update()
                {
                    ExternalMovementHelpers.Move();
                }
            }
            """,
            externalSource);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.EcsOpaqueUpdateAccess, diagnostic.Id);
    }

    [Fact]
    public async Task ExternalMethodWithAccessSummary_IsAccepted()
    {
        var externalSource =
            """
            using DCFApixels.DragonECS;
            using Karpik.Engine.Shared.ECS.Scheduling;

            public static class ExternalMovementHelpers
            {
                [Reads<Position>]
                public static void Read()
                {
                }
            }

            public struct Position : IEcsComponent
            {
                public int X;
            }
            """;

        var diagnostics = await AnalyzeAsync(
            """
            using Karpik.Engine.Core;

            public sealed class MovementSystem : ISystemUpdate
            {
                public void Update()
                {
                    ExternalMovementHelpers.Read();
                }
            }
            """,
            externalSource);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task BclPureMathCall_IsAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using System;
            using Karpik.Engine.Core;

            public sealed class MovementSystem : ISystemUpdate
            {
                public void Update()
                {
                    _ = Math.Abs(-1);
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task VirtualSourceMethodWithoutSummary_IsOpaque()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using Karpik.Engine.Core;

            public class MovementSystem : ISystemUpdate
            {
                public void Update()
                {
                    Touch();
                }

                protected virtual void Touch()
                {
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.EcsOpaqueUpdateAccess, diagnostic.Id);
    }

    [Fact]
    public async Task InterfaceDispatchWithAccessSummary_IsAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using DCFApixels.DragonECS;
            using Karpik.Engine.Core;
            using Karpik.Engine.Shared.ECS.Scheduling;

            public interface IMovementHelper
            {
                [Reads<Position>]
                void Read();
            }

            public sealed class MovementSystem : ISystemUpdate
            {
                private IMovementHelper _helper;

                public void Update()
                {
                    _helper.Read();
                }
            }

            public struct Position : IEcsComponent
            {
                public int X;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task MainThreadOnlyMethodCallFromUpdate_IsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using Karpik.Engine.Core;
            using Karpik.Engine.Shared.ECS.Scheduling;

            public sealed class MovementSystem : ISystemUpdate
            {
                public void Update()
                {
                    GraphicsApi.Draw();
                }
            }

            public static class GraphicsApi
            {
                [MainThreadOnly]
                public static void Draw()
                {
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.EcsMainThreadOnlyUpdateAccess, diagnostic.Id);
    }

    [Fact]
    public async Task MainThreadOnlyTypeCallFromUpdate_IsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using Karpik.Engine.Core;
            using Karpik.Engine.Shared.ECS.Scheduling;

            public sealed class MovementSystem : ISystemUpdate
            {
                public void Update()
                {
                    GraphicsApi.Draw();
                }
            }

            [MainThreadOnly]
            public static class GraphicsApi
            {
                public static void Draw()
                {
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.EcsMainThreadOnlyUpdateAccess, diagnostic.Id);
    }

    [Fact]
    public async Task TypeOfReflectionInsideUpdate_IsOpaque()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using System;
            using DCFApixels.DragonECS;
            using Karpik.Engine.Core;

            public sealed class MovementSystem : ISystemUpdate
            {
                public void Update()
                {
                    Type componentType = typeof(Position);
                }
            }

            public struct Position : IEcsComponent
            {
                public int X;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.EcsOpaqueUpdateAccess, diagnostic.Id);
    }

    [Fact]
    public async Task DragonWorldGetPool_IsInferredAsWrite()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using DCFApixels.DragonECS;
            using Karpik.Engine.Core;
            using Karpik.Engine.Shared.ECS.Scheduling;

            [Reads<Position>]
            public sealed class MovementSystem : ISystemUpdate
            {
                private EcsDefaultWorld _world;

                public void Update()
                {
                    _ = _world.GetPool<Position>();
                }
            }

            public struct Position : IEcsComponent
            {
                public int X;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.EcsAccessSummaryContradiction, diagnostic.Id);
    }

    [Fact]
    public async Task WrappedDefaultWorldGet_IsInferredAsWrite()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using DCFApixels.DragonECS;
            using Karpik.Engine.Core;
            using Karpik.Engine.Shared.ECS;
            using Karpik.Engine.Shared.ECS.Scheduling;

            [Reads<Position>]
            public sealed class MovementSystem : ISystemUpdate
            {
                private DefaultWorld _world;

                public void Update()
                {
                    _world.Get<Position>(1);
                }
            }

            public struct Position : IEcsComponent
            {
                public int X;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.EcsAccessSummaryContradiction, diagnostic.Id);
    }

    [Fact]
    public async Task WrappedDefaultWorldHas_IsInferredAsRead()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using DCFApixels.DragonECS;
            using Karpik.Engine.Core;
            using Karpik.Engine.Shared.ECS;
            using Karpik.Engine.Shared.ECS.Scheduling;

            [Reads<Position>]
            public sealed class MovementSystem : ISystemUpdate
            {
                private DefaultWorld _world;

                public void Update()
                {
                    _ = _world.Has<Position>(1);
                }
            }

            public struct Position : IEcsComponent
            {
                public int X;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task WrappedDefaultWorldTryGet_IsInferredAsRead()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using DCFApixels.DragonECS;
            using Karpik.Engine.Core;
            using Karpik.Engine.Shared.ECS;
            using Karpik.Engine.Shared.ECS.Scheduling;

            [Reads<Position>]
            public sealed class MovementSystem : ISystemUpdate
            {
                private DefaultWorld _world;
                private EcsPool<Position> _pool;

                public void Update()
                {
                    _ = _world.TryGet<Position>(1, out var position, _pool);
                }
            }

            public struct Position : IEcsComponent
            {
                public int X;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("_world.Add(1, new Position());")]
    [InlineData("_world.Set(1, new Position());")]
    [InlineData("_world.Del<Position>(1);")]
    public async Task WrappedDefaultWorldComponentWriteMethods_AreInferredAsWrite(string call)
    {
        var diagnostics = await AnalyzeAsync(
            $$"""
            using DCFApixels.DragonECS;
            using Karpik.Engine.Core;
            using Karpik.Engine.Shared.ECS;
            using Karpik.Engine.Shared.ECS.Scheduling;

            [Reads<Position>]
            public sealed class MovementSystem : ISystemUpdate
            {
                private DefaultWorld _world;

                public void Update()
                {
                    {{call}}
                }
            }

            public struct Position : IEcsComponent
            {
                public int X;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.EcsAccessSummaryContradiction, diagnostic.Id);
    }

    [Fact]
    public async Task WrappedDefaultWorldEvent_IsInferredAsWrite()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using DCFApixels.DragonECS;
            using Karpik.Engine.Core;
            using Karpik.Engine.Shared.DragonECS;
            using Karpik.Engine.Shared.ECS;
            using Karpik.Engine.Shared.ECS.Scheduling;

            [Reads<PositionEvent>]
            public sealed class MovementSystem : ISystemUpdate
            {
                private DefaultWorld _world;

                public void Update()
                {
                    _world.Event<PositionEvent>();
                }
            }

            public struct PositionEvent : IEcsComponentEvent
            {
                public int Source { get; set; }
                public int Target { get; set; }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.EcsAccessSummaryContradiction, diagnostic.Id);
    }

    [Fact]
    public async Task ManagedComponentSummary_IsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using DCFApixels.DragonECS;
            using Karpik.Engine.Core;
            using Karpik.Engine.Shared.ECS.Scheduling;

            [Reads<ManagedComponent>]
            public sealed class MovementSystem : ISystemUpdate
            {
                public void Update()
                {
                }
            }

            public struct ManagedComponent : IEcsComponent
            {
                public string Name;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.EcsUnsupportedManagedComponentSummary, diagnostic.Id);
    }

    private static Task<IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic>> AnalyzeAsync(
        string source,
        params string[] metadataReferenceSources)
    {
        return AnalyzerTestHarness
            .GetAnalyzerDiagnosticsAsync(new EcsUpdateSchedulerAnalyzer(), source, metadataReferenceSources)
            .ContinueWith(static task => (IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic>)task.Result, TaskScheduler.Default);
    }
}
