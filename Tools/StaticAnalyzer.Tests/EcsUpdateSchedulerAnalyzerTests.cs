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
