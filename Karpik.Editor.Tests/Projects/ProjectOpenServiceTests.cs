using Karpik.Editor;
using Karpik.Engine.ProjectModel;
using Xunit;

namespace Karpik.Editor.Tests.Projects;

public sealed class ProjectOpenServiceTests
{
    [Fact]
    public async Task OpenAsync_PerformsRawValidationBeforeMsBuildEvaluation()
    {
        using var solution = TestSolution.Create(validSdk: false);
        var inspector = new FakeInspector([]);
        var service = new ProjectOpenService(
            inspector,
            new FakeInstallationProvider(solution.EngineRoot),
            new FakeContextFactory());

        ProjectOpenResult result = await service.OpenAsync(
            solution.SolutionPath,
            new ProjectGeneration(1),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("KARPIK001"));
        Assert.Equal(0, inspector.CallCount);
    }

    [Fact]
    public async Task OpenAsync_ValidatesEvaluatedPropertiesItemsAndCreatesInactiveCandidate()
    {
        using var solution = TestSolution.Create();
        IReadOnlyList<MsBuildProjectEvaluation> evaluations = solution.CreateEvaluations();
        var factory = new FakeContextFactory();
        var service = new ProjectOpenService(
            new FakeInspector(evaluations),
            new FakeInstallationProvider(solution.EngineRoot),
            factory);

        ProjectOpenResult result = await service.OpenAsync(
            solution.SolutionPath,
            new ProjectGeneration(42),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        ActiveProjectContext candidate = Assert.IsType<ActiveProjectContext>(result.Candidate);
        Assert.Equal(Path.GetFullPath(solution.SolutionPath), candidate.SolutionPath);
        Assert.Equal(new ProjectGeneration(42), candidate.Generation);
        Assert.Equal(solution.ClientBundle, candidate.Runtime.ClientBundlePath);
        Assert.Equal(solution.ServerBundle, candidate.Runtime.ServerBundlePath);
        Assert.Equal(solution.ClientRunner, candidate.Runtime.ClientRunnerPath);
        Assert.Equal(solution.ServerRunner, candidate.Runtime.ServerRunnerPath);
        Assert.Equal(1, factory.CreateCount);
    }

    [Fact]
    public async Task OpenAsync_RejectsEvaluatedSideMismatch()
    {
        using var solution = TestSolution.Create();
        var evaluations = solution.CreateEvaluations().ToArray();
        evaluations[0] = evaluations[0] with { Side = "Server" };
        var service = new ProjectOpenService(
            new FakeInspector(evaluations),
            new FakeInstallationProvider(solution.EngineRoot),
            new FakeContextFactory());

        ProjectOpenResult result = await service.OpenAsync(
            solution.SolutionPath,
            new ProjectGeneration(1),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("evaluated KarpikSide"));
    }

    [Fact]
    public async Task OpenAsync_RejectsEvaluatedProjectReferenceMismatch()
    {
        using var solution = TestSolution.Create();
        var evaluations = solution.CreateEvaluations().ToArray();
        evaluations[0] = evaluations[0] with { ProjectReferences = [] };
        var service = new ProjectOpenService(
            new FakeInspector(evaluations),
            new FakeInstallationProvider(solution.EngineRoot),
            new FakeContextFactory());

        ProjectOpenResult result = await service.OpenAsync(
            solution.SolutionPath,
            new ProjectGeneration(1),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("ProjectReference"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/bundle")]
    public async Task OpenAsync_RejectsMissingOrRelativeRuntimeBundle(string bundlePath)
    {
        using var solution = TestSolution.Create();
        var evaluations = solution.CreateEvaluations().ToArray();
        evaluations[0] = evaluations[0] with { RuntimeBundlePath = bundlePath };
        var service = new ProjectOpenService(
            new FakeInspector(evaluations),
            new FakeInstallationProvider(solution.EngineRoot),
            new FakeContextFactory());

        ProjectOpenResult result = await service.OpenAsync(
            solution.SolutionPath,
            new ProjectGeneration(1),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("KarpikRuntimeBundlePath"));
    }

    [Fact]
    public async Task OpenAsync_RequiresExactlyOneClientAndServerRuntime()
    {
        using var solution = TestSolution.Create();
        var evaluations = solution.CreateEvaluations().ToArray();
        int server = Array.FindIndex(evaluations, evaluation => evaluation.Side == "Server");
        evaluations[server] = evaluations[server] with { Kind = "Tool" };
        var service = new ProjectOpenService(
            new FakeInspector(evaluations),
            new FakeInstallationProvider(solution.EngineRoot),
            new FakeContextFactory());

        ProjectOpenResult result = await service.OpenAsync(
            solution.SolutionPath,
            new ProjectGeneration(1),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("exactly one Runtime Client"));
    }

    [Fact]
    public async Task OpenAsync_RejectsLinkedSolutionBeforeStartingMsBuild()
    {
        using var solution = TestSolution.Create();
        string linkedSolution = Path.Combine(solution.Root, "Linked.slnx");
        try
        {
            File.CreateSymbolicLink(linkedSolution, solution.SolutionPath);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                         or IOException
                                         or PlatformNotSupportedException)
        {
            return;
        }

        var inspector = new FakeInspector(solution.CreateEvaluations());
        var service = new ProjectOpenService(
            inspector,
            new FakeInstallationProvider(solution.EngineRoot),
            new FakeContextFactory());

        ProjectOpenResult result = await service.OpenAsync(
            linkedSolution,
            new ProjectGeneration(1),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("link", StringComparison.OrdinalIgnoreCase)
                                                          || diagnostic.Contains("reparse", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, inspector.CallCount);
    }

    [Fact]
    public async Task OpenAsync_RejectsProjectBelowLinkedDirectoryBeforeReadingOrStartingMsBuild()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "KarpikEditorProjectLinkTests",
            Guid.NewGuid().ToString("N"));
        string game = Path.Combine(root, "Game");
        string outside = Path.Combine(root, "Outside");
        Directory.CreateDirectory(game);
        Directory.CreateDirectory(outside);
        try
        {
            string outsideProject = Path.Combine(outside, "Client.csproj");
            WriteStandaloneProject(outsideProject, "Client");
            string linkedDirectory = Path.Combine(game, "Client");
            try
            {
                Directory.CreateSymbolicLink(linkedDirectory, outside);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException
                                             or IOException
                                             or PlatformNotSupportedException)
            {
                return;
            }
            string solutionPath = Path.Combine(game, "Game.slnx");
            File.WriteAllText(solutionPath, """
                <Solution>
                  <Project Path="Client/Client.csproj" />
                </Solution>
                """);
            File.WriteAllText(Path.Combine(game, "global.json"), """
                { "msbuild-sdks": { "Karpik.Engine.Sdk": "0.6.0-test" } }
                """);
            var inspector = new FakeInspector([]);
            var service = new ProjectOpenService(
                inspector,
                new FakeInstallationProvider(Path.Combine(root, "engine")),
                new FakeContextFactory());

            ProjectOpenResult result = await service.OpenAsync(
                solutionPath,
                new ProjectGeneration(1),
                TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("link", StringComparison.OrdinalIgnoreCase)
                                                              || diagnostic.Contains("reparse", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(0, inspector.CallCount);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task OpenAsync_RejectsRawProjectReferenceBelowLinkedDirectoryBeforeMsBuild()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "KarpikEditorReferenceLinkTests",
            Guid.NewGuid().ToString("N"));
        string game = Path.Combine(root, "Game");
        string outside = Path.Combine(root, "Outside");
        Directory.CreateDirectory(game);
        Directory.CreateDirectory(outside);
        try
        {
            WriteStandaloneProject(Path.Combine(outside, "Shared.csproj"), "Shared");
            string linkedDirectory = Path.Combine(game, "LinkedShared");
            try
            {
                Directory.CreateSymbolicLink(linkedDirectory, outside);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException
                                             or IOException
                                             or PlatformNotSupportedException)
            {
                return;
            }
            string client = Path.Combine(game, "Client", "Client.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(client)!);
            File.WriteAllText(client, """
                <Project Sdk="Karpik.Engine.Sdk">
                  <PropertyGroup>
                    <KarpikProjectKind>Runtime</KarpikProjectKind>
                    <KarpikSide>Client</KarpikSide>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../../Game/LinkedShared/Shared.csproj" />
                  </ItemGroup>
                </Project>
                """);
            string solutionPath = Path.Combine(game, "Game.slnx");
            File.WriteAllText(solutionPath, """
                <Solution>
                  <Project Path="Client/Client.csproj" />
                </Solution>
                """);
            File.WriteAllText(Path.Combine(game, "global.json"), """
                { "msbuild-sdks": { "Karpik.Engine.Sdk": "0.6.0-test" } }
                """);
            var inspector = new FakeInspector([]);
            var service = new ProjectOpenService(
                inspector,
                new FakeInstallationProvider(Path.Combine(root, "engine")),
                new FakeContextFactory());

            ProjectOpenResult result = await service.OpenAsync(
                solutionPath,
                new ProjectGeneration(1),
                TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("link", StringComparison.OrdinalIgnoreCase)
                                                              || diagnostic.Contains("reparse", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(0, inspector.CallCount);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void WriteStandaloneProject(string path, string side)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"""
            <Project Sdk="Karpik.Engine.Sdk">
              <PropertyGroup>
                <KarpikProjectKind>Runtime</KarpikProjectKind>
                <KarpikSide>{side}</KarpikSide>
              </PropertyGroup>
            </Project>
            """);
    }

    private sealed class FakeInspector(IReadOnlyList<MsBuildProjectEvaluation> evaluations)
        : IMsBuildProjectInspector
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<MsBuildProjectEvaluation>> InspectAsync(
            KarpikSolutionModel solution,
            string engineRoot,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(evaluations);
        }
    }

    private sealed class FakeInstallationProvider(string engineRoot) : IEngineInstallationProvider
    {
        public EngineInstallationSelection Resolve(string sdkVersion) =>
            new(true, engineRoot, null);
    }

    private sealed class FakeContextFactory : IActiveProjectContextFactory
    {
        public int CreateCount { get; private set; }

        public ActiveProjectContext Create(
            KarpikSolutionModel solution,
            ProjectRuntimeDescriptor runtime,
            ProjectGeneration generation)
        {
            CreateCount++;
            return new ActiveProjectContext(solution, runtime, generation, NullActiveProjectLifetime.Instance);
        }
    }

    private sealed class TestSolution : IDisposable
    {
        private TestSolution(string root)
        {
            Root = root;
            Directory.CreateDirectory(root);
        }

        public string Root { get; }
        public string SolutionPath => Path.Combine(Root, "Game.slnx");
        public string EngineRoot => Path.Combine(Root, "engine");
        public string ClientBundle => Path.Combine(Root, "Client", "bin", "karpik-bundle");
        public string ServerBundle => Path.Combine(Root, "Server", "bin", "karpik-bundle");
        public string ClientRunner => Path.Combine(EngineRoot, "runners", "client", RunnerName);
        public string ServerRunner => Path.Combine(EngineRoot, "runners", "server", RunnerName);
        private string SharedProject => Path.Combine(Root, "Shared", "Shared.csproj");
        private string ClientProject => Path.Combine(Root, "Client", "Client.csproj");
        private string ServerProject => Path.Combine(Root, "Server", "Server.csproj");
        private static string RunnerName => OperatingSystem.IsWindows()
            ? "Karpik.Engine.Core.Runner.exe"
            : "Karpik.Engine.Core.Runner";

        public static TestSolution Create(bool validSdk = true)
        {
            var solution = new TestSolution(Path.Combine(
                Path.GetTempPath(),
                "KarpikEditorProjectOpenTests",
                Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(solution.EngineRoot);
            WriteProject(solution.SharedProject, validSdk ? "Karpik.Engine.Sdk" : "Microsoft.NET.Sdk", "Shared");
            WriteProject(solution.ClientProject, "Karpik.Engine.Sdk", "Client", "../Shared/Shared.csproj");
            WriteProject(solution.ServerProject, "Karpik.Engine.Sdk", "Server", "../Shared/Shared.csproj");
            File.WriteAllText(solution.SolutionPath, """
                <Solution>
                  <Project Path="Shared/Shared.csproj" />
                  <Project Path="Client/Client.csproj" />
                  <Project Path="Server/Server.csproj" />
                </Solution>
                """);
            File.WriteAllText(Path.Combine(solution.Root, "global.json"), """
                { "msbuild-sdks": { "Karpik.Engine.Sdk": "0.6.0-test" } }
                """);
            return solution;
        }

        public IReadOnlyList<MsBuildProjectEvaluation> CreateEvaluations() =>
        [
            Evaluation(ClientProject, "Client", ClientBundle, [SharedProject]),
            Evaluation(ServerProject, "Server", ServerBundle, [SharedProject]),
            Evaluation(SharedProject, "Shared", "", [])
        ];

        private MsBuildProjectEvaluation Evaluation(
            string projectPath,
            string side,
            string bundle,
            IReadOnlyList<string> references) =>
            new(
                projectPath,
                "Runtime",
                side,
                bundle,
                EngineRoot,
                Path.ChangeExtension(projectPath, ".dll"),
                references);

        private static void WriteProject(
            string path,
            string sdk,
            string side,
            string? reference = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string referenceXml = reference is null
                ? ""
                : $"<ItemGroup><ProjectReference Include=\"{reference}\" /></ItemGroup>";
            File.WriteAllText(path, $"""
                <Project Sdk="{sdk}">
                  <PropertyGroup>
                    <KarpikProjectKind>Runtime</KarpikProjectKind>
                    <KarpikSide>{side}</KarpikSide>
                  </PropertyGroup>
                  {referenceXml}
                </Project>
                """);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
