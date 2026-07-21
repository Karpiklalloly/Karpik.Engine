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
    public async Task OpenAsync_RejectsRuntimeBundleOutsideActiveGameRoot()
    {
        using var solution = TestSolution.Create();
        string foreignBundle = Path.Combine(
            Path.GetTempPath(),
            $"KarpikForeignBundle-{Guid.NewGuid():N}",
            "karpik-bundle");
        var evaluations = solution.CreateEvaluations().ToArray();
        evaluations[0] = evaluations[0] with { RuntimeBundlePath = foreignBundle };
        var service = new ProjectOpenService(
            new FakeInspector(evaluations),
            new FakeInstallationProvider(solution.EngineRoot),
            new FakeContextFactory());

        ProjectOpenResult result = await service.OpenAsync(
            solution.SolutionPath,
            new ProjectGeneration(1),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Contains("active game root", StringComparison.Ordinal));
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

    [Fact]
    public async Task OpenAsync_HoldsInputLeaseThroughEvaluationAndBlocksProjectReplacement()
    {
        using var solution = TestSolution.Create();
        var inspector = new FakeInspector(solution.CreateEvaluations());
        var hook = new ReplacementAttemptHook(solution.ClientProjectPath);
        var service = new ProjectOpenService(
            inspector,
            new FakeInstallationProvider(solution.EngineRoot),
            new FakeContextFactory(),
            hook);

        ProjectOpenResult result = await service.OpenAsync(
            solution.SolutionPath,
            new ProjectGeneration(1),
            TestContext.Current.CancellationToken);

        if (!OperatingSystem.IsWindows())
        {
            Assert.False(result.IsSuccess);
            Assert.Equal(0, inspector.CallCount);
            return;
        }

        Assert.True(hook.ReplacementBlocked);
        Assert.True(hook.WriteBlocked);
        Assert.True(hook.DirectoryRenameBlocked);
        Assert.False(hook.ReplacementSucceeded);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, inspector.CallCount);
    }

    [Fact]
    public void ProjectInputLease_PortableMirrorCopiesOnlyBuildMetadataAndRemapsPathResults()
    {
        using var solution = TestSolution.Create();
        string source = Path.Combine(
            Path.GetDirectoryName(solution.ClientProjectPath)!,
            "Program.cs");
        string asset = Path.Combine(solution.Root, "Content", "texture.bin");
        File.WriteAllText(source, "class Program {}");
        Directory.CreateDirectory(Path.GetDirectoryName(asset)!);
        File.WriteAllText(asset, "asset");
        File.WriteAllText(
            Path.Combine(solution.Root, "Directory.Solution.targets"),
            "<Project><Import Project=\"Solution.targets\" Sdk=\"Karpik.Engine.Sdk\" /></Project>");
        File.WriteAllText(
            Path.Combine(solution.Root, "Directory.Build.props"),
            "<Project><PropertyGroup><LangVersion>preview</LangVersion></PropertyGroup></Project>");
        File.WriteAllText(
            Path.Combine(solution.Root, "NuGet.Config"),
            "<configuration />");

        using ProjectInputLease lease = ProjectInputLease.AcquirePortableMirror(
            solution.SolutionPath);
        var raw = new KarpikSolutionReader().Read(solution.SolutionPath, lease);
        KarpikSolutionModel evaluation = lease.CreateEvaluationSolution(raw);
        string evaluationRoot = Assert.IsType<string>(lease.EvaluationRoot);

        Assert.True(File.Exists(evaluation.SolutionPath));
        Assert.All(evaluation.Projects, project => Assert.True(File.Exists(project.ProjectPath)));
        Assert.True(File.Exists(Path.Combine(evaluationRoot, "global.json")));
        Assert.True(File.Exists(Path.Combine(evaluationRoot, "Directory.Solution.targets")));
        Assert.True(File.Exists(Path.Combine(evaluationRoot, "Directory.Build.props")));
        Assert.True(File.Exists(Path.Combine(evaluationRoot, "NuGet.Config")));
        Assert.False(File.Exists(Path.Combine(
            evaluationRoot,
            Path.GetRelativePath(solution.Root, source))));
        Assert.False(File.Exists(Path.Combine(
            evaluationRoot,
            Path.GetRelativePath(solution.Root, asset))));

        KarpikProjectDescriptor client = evaluation.Projects.Single(
            project => project.Side == KarpikProjectSide.Client);
        string mirrorBundle = Path.Combine(
            evaluationRoot,
            Path.GetRelativePath(solution.Root, solution.ClientBundle));
        string mirrorTarget = Path.Combine(
            Path.GetDirectoryName(client.ProjectPath)!,
            "bin",
            "Client.dll");
        IReadOnlyList<MsBuildProjectEvaluation> remapped = lease.RemapEvaluations(
        [
            new MsBuildProjectEvaluation(
                client.ProjectPath,
                "Runtime",
                "Client",
                mirrorBundle,
                solution.EngineRoot,
                mirrorTarget,
                client.ProjectReferences)
        ]);

        MsBuildProjectEvaluation result = Assert.Single(remapped);
        Assert.Equal(solution.ClientProjectPath, result.ProjectPath);
        Assert.Equal(solution.ClientBundle, result.RuntimeBundlePath);
        Assert.StartsWith(solution.Root, result.TargetPath);
        Assert.Equal(solution.EngineRoot, result.EngineRoot);
        Assert.All(
            result.ProjectReferences,
            reference => Assert.StartsWith(solution.Root, reference));

        Assert.Throws<InvalidDataException>(() => lease.RemapEvaluations(
        [
            new MsBuildProjectEvaluation(
                client.ProjectPath,
                "Runtime",
                "Client",
                Path.Combine(Path.GetDirectoryName(evaluationRoot)!, "escaped-bundle"),
                solution.EngineRoot,
                mirrorTarget,
                client.ProjectReferences)
        ]));
    }

    [Fact]
    public void ProjectInputLease_PortableMirrorIncludesTransitiveProjectMetadata()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "KarpikEditorPortableLeaseTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string client = Path.Combine(root, "Client", "Client.csproj");
            string shared = Path.Combine(root, "Shared", "Shared.csproj");
            string foundation = Path.Combine(root, "Foundation", "Foundation.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(client)!);
            Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
            Directory.CreateDirectory(Path.GetDirectoryName(foundation)!);
            File.WriteAllText(client, """
                <Project Sdk="Karpik.Engine.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="../Shared/Shared.csproj" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(shared, """
                <Project Sdk="Karpik.Engine.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="../Foundation/Foundation.csproj" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(foundation, """<Project Sdk="Karpik.Engine.Sdk" />""");
            string solutionPath = Path.Combine(root, "Game.slnx");
            File.WriteAllText(solutionPath, """
                <Solution>
                  <Project Path="Client/Client.csproj" />
                </Solution>
                """);
            File.WriteAllText(Path.Combine(root, "global.json"), """
                { "msbuild-sdks": { "Karpik.Engine.Sdk": "0.6.0-test" } }
                """);

            using ProjectInputLease lease =
                ProjectInputLease.AcquirePortableMirror(solutionPath);
            string evaluationRoot = Assert.IsType<string>(lease.EvaluationRoot);

            Assert.True(File.Exists(Path.Combine(
                evaluationRoot,
                Path.GetRelativePath(root, shared))));
            Assert.True(File.Exists(Path.Combine(
                evaluationRoot,
                Path.GetRelativePath(root, foundation))));
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
    public async Task OpenAsync_RejectsExplicitProjectImportBeforeMsBuild()
    {
        using var solution = TestSolution.Create();
        string props = Path.Combine(
            Path.GetDirectoryName(solution.ClientProjectPath)!,
            "Custom.props");
        File.WriteAllText(props, "<Project />");
        string project = File.ReadAllText(solution.ClientProjectPath);
        File.WriteAllText(
            solution.ClientProjectPath,
            project.Replace(
                "</Project>",
                "<Import Project=\"Custom.props\" /></Project>",
                StringComparison.Ordinal));
        var inspector = new FakeInspector(solution.CreateEvaluations());
        var service = new ProjectOpenService(
            inspector,
            new FakeInstallationProvider(solution.EngineRoot),
            new FakeContextFactory());

        ProjectOpenResult result = await service.OpenAsync(
            solution.SolutionPath,
            new ProjectGeneration(1),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Contains("Project imports", StringComparison.Ordinal));
        Assert.Equal(0, inspector.CallCount);
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
            CancellationToken cancellationToken,
            ProjectInputLease? inputLease = null)
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
        public string ClientProjectPath => ClientProject;
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

    private sealed class ReplacementAttemptHook(string projectPath) : IProjectInputLeaseHook
    {
        public bool ReplacementBlocked { get; private set; }
        public bool ReplacementSucceeded { get; private set; }
        public bool WriteBlocked { get; private set; }
        public bool DirectoryRenameBlocked { get; private set; }

        public void AfterLeaseAcquired(string solutionPath)
        {
            string moved = projectPath + ".moved";
            try
            {
                File.Move(projectPath, moved);
                ReplacementSucceeded = true;
                File.WriteAllText(projectPath, """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <KarpikProjectKind>Runtime</KarpikProjectKind>
                        <KarpikSide>Server</KarpikSide>
                      </PropertyGroup>
                    </Project>
                    """);
            }
            catch (IOException)
            {
                ReplacementBlocked = true;
            }
            finally
            {
                if (File.Exists(moved))
                {
                    if (File.Exists(projectPath))
                    {
                        File.Delete(projectPath);
                    }
                    File.Move(moved, projectPath);
                }
            }

            try
            {
                File.WriteAllText(projectPath, "<Project />");
            }
            catch (IOException)
            {
                WriteBlocked = true;
            }

            string directory = Path.GetDirectoryName(projectPath)!;
            string movedDirectory = directory + ".moved";
            try
            {
                Directory.Move(directory, movedDirectory);
            }
            catch (IOException)
            {
                DirectoryRenameBlocked = true;
            }
            finally
            {
                if (Directory.Exists(movedDirectory))
                {
                    Directory.Move(movedDirectory, directory);
                }
            }
        }
    }
}
