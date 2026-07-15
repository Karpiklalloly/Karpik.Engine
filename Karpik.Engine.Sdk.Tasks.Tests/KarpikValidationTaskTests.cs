using System.Collections;
using System.Xml.Linq;
using Karpik.Engine.ProjectModel;
using Karpik.Engine.Sdk.Tasks;
using Microsoft.Build.Framework;
using Xunit;

public sealed class KarpikValidationTaskTests
{
    [Fact]
    public void SdkTargetsTreatUndefinedSolutionPathAsADirectBuild()
    {
        var targets = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Sdk.targets"));
        var solutionTarget = targets.Root!.Elements("Target")
            .Single(target => (string?)target.Attribute("Name") == "ValidateKarpikSolution");
        var directTarget = targets.Root.Elements("Target")
            .Single(target => (string?)target.Attribute("Name") == "ValidateKarpikProjectReferences");

        Assert.Contains("'$(SolutionPath)' != '*Undefined*'", (string?)solutionTarget.Attribute("Condition"));
        Assert.Contains("'$(SolutionPath)' == '*Undefined*'", (string?)directTarget.Attribute("Condition"));
    }

    [Fact]
    public void DirectorySolutionTargetsImportsThePackageSolutionHook()
    {
        var template = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Directory.Solution.targets"));
        var import = Assert.Single(template.Root!.Elements("Import"));

        Assert.Equal("Solution.targets", (string?)import.Attribute("Project"));
        Assert.Equal("Karpik.Engine.Sdk", (string?)import.Attribute("Sdk"));
    }

    [Fact]
    public void PackageSolutionHookValidatesBeforeTheSolutionBuildTarget()
    {
        var targets = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Solution.targets"));
        var target = Assert.Single(targets.Root!.Elements("Target"));
        var validationTask = Assert.Single(target.Elements("ValidateKarpikSolutionTask"));

        Assert.Equal("ValidateKarpikSolutionBeforeBuild", (string?)target.Attribute("Name"));
        Assert.Equal("Build", (string?)target.Attribute("BeforeTargets"));
        Assert.Equal("$(SolutionPath)", (string?)validationTask.Attribute("SolutionPath"));
    }

    [Fact]
    public void SolutionTaskAcceptsAValidSolution()
    {
        WithProjectTree(tree =>
        {
            tree.AddProject("Shared/Shared.csproj", side: "Shared");
            var engine = new FakeBuildEngine();
            var task = new ValidateKarpikSolutionTask
            {
                BuildEngine = engine,
                SolutionPath = tree.WriteSolution()
            };

            Assert.True(task.Execute());
            Assert.Empty(engine.Errors);
        });
    }

    [Fact]
    public void SolutionTaskLogsStableDiagnosticsForForeignProjects()
    {
        WithProjectTree(tree =>
        {
            tree.AddProject("Shared/Shared.csproj", side: "Shared");
            tree.AddProject("Foreign/Foreign.csproj", sdk: "Microsoft.NET.Sdk", kind: null, side: null);
            var engine = new FakeBuildEngine();
            var task = new ValidateKarpikSolutionTask
            {
                BuildEngine = engine,
                SolutionPath = tree.WriteSolution()
            };

            Assert.False(task.Execute());
            Assert.Equal(
                [KarpikDiagnosticCodes.MissingSdk, KarpikDiagnosticCodes.InvalidProjectKind, KarpikDiagnosticCodes.InvalidProjectSide],
                engine.Errors.Select(error => error.Code));
        });
    }

    [Fact]
    public void DirectBuildTaskValidatesTheTransitiveProjectReferenceGraph()
    {
        WithProjectTree(tree =>
        {
            var server = tree.AddProject("Server/Server.csproj", side: "Server");
            var client = tree.AddProject("Client/Client.csproj", side: "Client", references: [server]);
            var engine = new FakeBuildEngine();
            var task = new ValidateKarpikProjectReferencesTask
            {
                BuildEngine = engine,
                ProjectPath = Path.Combine(tree.RootPath, client)
            };

            Assert.False(task.Execute());
            Assert.Equal([KarpikDiagnosticCodes.ForbiddenSideDependency], engine.Errors.Select(error => error.Code));
        });
    }

    [Fact]
    public void DirectBuildTaskAcceptsAValidTransitiveProjectReferenceGraph()
    {
        WithProjectTree(tree =>
        {
            var shared = tree.AddProject("Shared/Shared.csproj", side: "Shared");
            var client = tree.AddProject("Client/Client.csproj", side: "Client", references: [shared]);
            var engine = new FakeBuildEngine();
            var task = new ValidateKarpikProjectReferencesTask
            {
                BuildEngine = engine,
                ProjectPath = Path.Combine(tree.RootPath, client)
            };

            Assert.True(task.Execute());
            Assert.Empty(engine.Errors);
        });
    }

    [Fact]
    public void TaskFailuresUseAStableStructuralDiagnostic()
    {
        var engine = new FakeBuildEngine();
        var task = new ValidateKarpikSolutionTask
        {
            BuildEngine = engine,
            SolutionPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Missing.slnx")
        };

        Assert.False(task.Execute());
        Assert.Equal(KarpikDiagnosticCodes.InvalidSolutionProject, Assert.Single(engine.Errors).Code);
    }

    [Fact]
    public void MalformedSolutionsUseAStableStructuralDiagnostic()
    {
        WithProjectTree(tree =>
        {
            var solutionPath = Path.Combine(tree.RootPath, "Broken.slnx");
            File.WriteAllText(solutionPath, "<Solution>");
            var engine = new FakeBuildEngine();
            var task = new ValidateKarpikSolutionTask
            {
                BuildEngine = engine,
                SolutionPath = solutionPath
            };

            Assert.False(task.Execute());
            Assert.Equal(KarpikDiagnosticCodes.InvalidSolutionProject, Assert.Single(engine.Errors).Code);
        });
    }

    private static void WithProjectTree(Action<TestProjectTree> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "KarpikSdkTaskTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            test(new TestProjectTree(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestProjectTree(string rootPath)
    {
        private readonly List<string> _solutionProjects = [];

        public string RootPath { get; } = rootPath;

        public string AddProject(
            string relativePath,
            string? sdk = "Karpik.Engine.Sdk",
            string? kind = "Runtime",
            string? side = "Shared",
            IReadOnlyList<string>? references = null)
        {
            var project = new XElement("Project");
            if (sdk != null)
            {
                project.Add(new XAttribute("Sdk", sdk));
            }

            var properties = new XElement("PropertyGroup");
            if (kind != null)
            {
                properties.Add(new XElement("KarpikProjectKind", kind));
            }
            if (side != null)
            {
                properties.Add(new XElement("KarpikSide", side));
            }
            project.Add(properties);

            if (references is { Count: > 0 })
            {
                project.Add(new XElement("ItemGroup", references.Select(reference =>
                    new XElement("ProjectReference", new XAttribute(
                        "Include",
                        Path.GetRelativePath(Path.GetDirectoryName(relativePath)!, reference))))));
            }

            var fullPath = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            new XDocument(project).Save(fullPath);
            _solutionProjects.Add(relativePath);
            return relativePath;
        }

        public string WriteSolution()
        {
            var path = Path.Combine(RootPath, "Game.slnx");
            new XDocument(new XElement("Solution", _solutionProjects.Select(project =>
                new XElement("Project", new XAttribute("Path", project))))).Save(path);
            return path;
        }
    }

    private sealed class FakeBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];

        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);
        public void LogWarningEvent(BuildWarningEventArgs e) { }
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogCustomEvent(CustomBuildEventArgs e) { }

        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            IDictionary globalProperties,
            IDictionary targetOutputs) => throw new NotSupportedException();
    }
}
