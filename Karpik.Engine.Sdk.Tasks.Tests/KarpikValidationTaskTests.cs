using System.Collections;
using System.Xml.Linq;
using Karpik.Engine.ProjectModel;
using Karpik.Engine.Sdk.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xunit;

public sealed class KarpikValidationTaskTests
{
    [Fact]
    public void SdkWiresCoreCodegenAnalyzerIntoExternalProjects()
    {
        var targets = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Sdk.targets"));

        XElement codegenPath = Assert.Single(
            targets.Root!.Elements("PropertyGroup").Elements("_KarpikCoreCodegenAssembly"));
        XElement analyzer = targets.Root.Elements("ItemGroup").Elements("Analyzer")
            .Single(element => (string?)element.Attribute("Include") == "$(_KarpikCoreCodegenAssembly)");

        Assert.Contains("Karpik.Engine.Core.Codegen.dll", codegenPath.Value, StringComparison.Ordinal);
        Assert.Equal("$(_KarpikCoreCodegenAssembly)", (string?)analyzer.Attribute("Include"));
        Assert.Equal(
            "Exists('$(_KarpikCoreCodegenAssembly)')",
            (string?)analyzer.Attribute("Condition"));
    }

    [Fact]
    public void SdkWiresNetworkCodegenExactlyOnceIntoRuntimeProjects()
    {
        var targets = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Sdk.targets"));

        XElement codegenPath = Assert.Single(
            targets.Root!.Elements("PropertyGroup").Elements("_KarpikNetworkCodegenAssembly"));
        XElement analyzer = Assert.Single(
            targets.Root.Elements("ItemGroup").Elements("Analyzer"),
            element => (string?)element.Attribute("Include") == "$(_KarpikNetworkCodegenAssembly)");

        Assert.Contains("Network.Codegen.dll", codegenPath.Value, StringComparison.Ordinal);
        Assert.Equal(
            "'$(KarpikProjectKind)' == 'Runtime' And Exists('$(_KarpikNetworkCodegenAssembly)')",
            (string?)analyzer.Attribute("Condition"));
    }

    [Fact]
    public void SdkResolvesEngineRootBeforeCreatingRuntimeReferences()
    {
        var targets = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Sdk.targets"));
        XElement usingTask = targets.Root!.Elements("UsingTask")
            .Single(element => (string?)element.Attribute("TaskName") ==
                               "Karpik.Engine.Sdk.Tasks.ResolveKarpikEngineRootTask");
        XElement target = targets.Root.Elements("Target")
            .Single(element => (string?)element.Attribute("Name") ==
                               "_KarpikResolveEngineReferenceAssemblies");
        XElement resolve = Assert.Single(target.Elements("ResolveKarpikEngineRootTask"));
        XElement output = Assert.Single(resolve.Elements("Output"));

        Assert.Equal("$(_KarpikSdkTaskAssembly)", (string?)usingTask.Attribute("AssemblyFile"));
        Assert.Equal("ResolveAssemblyReferences", (string?)target.Attribute("BeforeTargets"));
        Assert.Equal(
            "'$(KarpikProjectKind)' == 'Runtime' Or ('$(KarpikProjectKind)' == 'Tool' And '$(KarpikCompositionMode)' == 'Static')",
            (string?)target.Attribute("Condition"));
        Assert.Equal("$(_KarpikMsBuildSdkVersion)", (string?)resolve.Attribute("SdkVersion"));
        Assert.Equal("$(KarpikEngineRoot)", (string?)resolve.Attribute("ExplicitRoot"));
        Assert.Equal(
            "$(KarpikLocalApplicationDataRoot)",
            (string?)resolve.Attribute("LocalApplicationDataRoot"));
        Assert.Equal("ResolvedRoot", (string?)output.Attribute("TaskParameter"));
        Assert.Equal("KarpikEngineRoot", (string?)output.Attribute("PropertyName"));
    }

    [Fact]
    public void SdkProvidesAutofacFromInstalledEngineToRuntimeProjects()
    {
        var targets = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Sdk.targets"));
        XElement target = targets.Root!.Elements("Target")
            .Single(element => (string?)element.Attribute("Name") ==
                               "_KarpikResolveEngineReferenceAssemblies");
        XElement reference = target.Descendants("Reference")
            .Single(element => (string?)element.Attribute("Include") == "Autofac");

        Assert.Equal(
            @"$(KarpikEngineRoot)\runners\server\Autofac.dll",
            reference.Element("HintPath")?.Value);
        Assert.Equal("false", reference.Element("Private")?.Value);
    }

    [Fact]
    public void SdkProvidesCompositionAttributesRequiredByGeneratedRuntimeServices()
    {
        var targets = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Sdk.targets"));
        XElement target = targets.Root!.Elements("Target")
            .Single(element => (string?)element.Attribute("Name") ==
                               "_KarpikResolveEngineReferenceAssemblies");
        XElement reference = target.Descendants("Reference")
            .Single(element => (string?)element.Attribute("Include") ==
                               "System.Composition.AttributedModel");

        Assert.Equal(
            @"$(KarpikEngineRoot)\runners\server\System.Composition.AttributedModel.dll",
            reference.Element("HintPath")?.Value);
        Assert.Equal("false", reference.Element("Private")?.Value);
    }

    [Fact]
    public void SdkWiresRuntimeBundleOnlyForClientAndServerRuntimeProjects()
    {
        var props = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Sdk.props"));
        var targets = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Sdk.targets"));
        var bundleTarget = targets.Root!.Elements("Target")
            .Single(target => (string?)target.Attribute("Name") == "BuildKarpikRuntimeBundle");
        var bundleTask = Assert.Single(bundleTarget.Elements("BuildKarpikRuntimeBundleTask"));

        Assert.Contains(props.Descendants("KarpikRuntimeBundleDirectoryName"), element => element.Value == "karpik-bundle");
        Assert.Equal("CopyFilesToOutputDirectory", (string?)bundleTarget.Attribute("AfterTargets"));
        string condition = Assert.IsType<XAttribute>(bundleTarget.Attribute("Condition")).Value;
        Assert.Contains("'$(KarpikProjectKind)' == 'Runtime'", condition);
        Assert.Contains("'$(KarpikSide)' == 'Client' Or '$(KarpikSide)' == 'Server'", condition);
        Assert.Equal("$(KarpikRuntimeBundlePath)", (string?)bundleTask.Attribute("BundlePath"));
        Assert.Equal("$(TargetPath)", (string?)bundleTask.Attribute("PrimaryAssembly"));
        Assert.Equal("@(_KarpikBundleContent)", (string?)bundleTask.Attribute("Content"));
        Assert.Equal("@(_KarpikBundleMod)", (string?)bundleTask.Attribute("Mods"));

        XElement[] bundleMods = bundleTarget.Descendants("_KarpikBundleMod").ToArray();
        Assert.Equal(2, bundleMods.Length);
        XElement bundleModInclude = Assert.Single(bundleMods, item => item.Attribute("Include") is not null);
        Assert.Equal(@"$(TargetDir)Mods\**\*", (string?)bundleModInclude.Attribute("Include"));
        Assert.Null(bundleModInclude.Attribute("TargetPath"));
        Assert.Null(bundleModInclude.Element("TargetPath"));
        XElement bundleModUpdate = Assert.Single(bundleMods, item => item.Attribute("Update") is not null);
        Assert.Equal("@(_KarpikBundleMod)", (string?)bundleModUpdate.Attribute("Update"));
        Assert.Equal(
            "%(_KarpikBundleMod.RecursiveDir)%(_KarpikBundleMod.Filename)%(_KarpikBundleMod.Extension)",
            (string?)bundleModUpdate.Attribute("TargetPath"));
        Assert.Equal("$(KarpikSide)", (string?)bundleTask.Attribute("Side"));
        XElement[] contentItems = bundleTarget.Descendants("_KarpikBundleContent").ToArray();
        Assert.Equal(2, contentItems.Length);
        XElement contentInclude = Assert.Single(contentItems, item => item.Attribute("Include") is not null);
        Assert.StartsWith("$(TargetDir)Content", (string?)contentInclude.Attribute("Include"));
        Assert.Null(contentInclude.Attribute("TargetPath"));
        Assert.Null(contentInclude.Element("TargetPath"));
        XElement contentUpdate = Assert.Single(contentItems, item => item.Attribute("Update") is not null);
        Assert.Equal("@(_KarpikBundleContent)", (string?)contentUpdate.Attribute("Update"));
        Assert.Equal(
            "%(_KarpikBundleContent.RecursiveDir)%(_KarpikBundleContent.Filename)%(_KarpikBundleContent.Extension)",
            (string?)contentUpdate.Attribute("TargetPath"));
        Assert.DoesNotContain("$(MSBuildProjectDirectory)", (string?)contentInclude.Attribute("Include"));

        XElement bundlePath = targets.Root.Elements("PropertyGroup")
            .SelectMany(group => group.Elements("KarpikRuntimeBundlePath"))
            .Single();
        Assert.Equal("'$(KarpikRuntimeBundlePath)' == ''", (string?)bundlePath.Attribute("Condition"));
        Assert.Contains("$(TargetDir)", bundlePath.Value);
        Assert.Empty(bundleTarget.Descendants("KarpikRuntimeBundlePath"));
        var evaluatedElements = targets.Root.Elements().ToList();
        Assert.True(evaluatedElements.IndexOf(targets.Root.Element("Import")!)
                    < evaluatedElements.IndexOf(bundlePath.Parent!));
    }

    [Fact]
    public void SdkWiresStaticCompositionModeIntoRuntimeBundling()
    {
        var targets = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Sdk.targets"));
        XElement bundleTarget = targets.Root!.Elements("Target")
            .Single(target => (string?)target.Attribute("Name") == "BuildKarpikRuntimeBundle");
        XElement bundleTask = Assert.Single(bundleTarget.Elements("BuildKarpikRuntimeBundleTask"));

        Assert.Equal("$(KarpikCompositionMode)", (string?)bundleTask.Attribute("CompositionMode"));

        XElement assemblyStaging = bundleTarget.Descendants("_KarpikBundleAssembly")
            .Single(item => item.Attribute("Include") is not null);
        Assert.Contains("'$(KarpikCompositionMode)' != 'Static'", (string?)assemblyStaging.Attribute("Condition"));

        XElement nativeInclude = Assert.Single(bundleTarget.Descendants("_KarpikBundleNative"), item =>
            (string?)item.Attribute("Include") == "@(NativeCopyLocalItems)");
        Assert.Equal("'$(KarpikCompositionMode)' == 'Static'", (string?)nativeInclude.Attribute("Condition"));
        XElement nativeUpdate = Assert.Single(bundleTarget.Descendants("_KarpikBundleNative"), item =>
            (string?)item.Attribute("Update") == "@(_KarpikBundleNative)" &&
            (string?)item.Attribute("TargetPath") == "%(_KarpikBundleNative.DestinationSubPath)");
        Assert.Null(nativeUpdate.Attribute("Condition"));
        XElement flatNativeUpdate = Assert.Single(bundleTarget.Descendants("_KarpikBundleNative"), item =>
            (string?)item.Attribute("Update") == "@(_KarpikBundleNative)" &&
            ((string?)item.Attribute("Condition") ?? string.Empty).Contains("== ''", StringComparison.Ordinal));
        Assert.Equal(
            @"native\%(_KarpikBundleNative.Filename)%(_KarpikBundleNative.Extension)",
            (string?)flatNativeUpdate.Attribute("TargetPath"));
    }

    [Fact]
    public void SdkTargetsUseDistinctRestoreAndLateBuildGates()
    {
        var targets = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Sdk.targets"));
        var restoreTarget = targets.Root!.Elements("Target")
            .Single(target => (string?)target.Attribute("Name") == "ValidateKarpikProjectReferencesBeforeRestore");
        var lateBuildTarget = targets.Root.Elements("Target")
            .Single(target => (string?)target.Attribute("Name") == "ValidateKarpikProjectReferencesBeforeBuild");
        var restoreTask = Assert.Single(restoreTarget.Elements("ValidateKarpikProjectReferencesTask"));
        var lateBuildTask = Assert.Single(lateBuildTarget.Elements("ValidateKarpikProjectReferencesTask"));

        Assert.Equal("_GenerateRestoreProjectPathWalk", (string?)restoreTarget.Attribute("BeforeTargets"));
        Assert.Equal("AssignProjectConfiguration", (string?)lateBuildTarget.Attribute("BeforeTargets"));
        Assert.DoesNotContain(";", (string?)restoreTarget.Attribute("BeforeTargets"));
        Assert.DoesNotContain(";", (string?)lateBuildTarget.Attribute("BeforeTargets"));
        Assert.Equal("@(ProjectReference->'%(FullPath)')", (string?)restoreTask.Attribute("ProjectReferences"));
        Assert.Equal("@(ProjectReference->'%(FullPath)')", (string?)lateBuildTask.Attribute("ProjectReferences"));
        Assert.Same(lateBuildTarget, targets.Root.Elements().Last());
        var elements = targets.Root.Elements().ToList();
        var importIndex = elements.IndexOf(targets.Root.Element("Import")!);
        Assert.True(importIndex < elements.IndexOf(restoreTarget));
        Assert.True(importIndex < elements.IndexOf(lateBuildTarget));
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
    public void DirectBuildTaskRejectsEvaluatedReferencesAbsentFromRawProject()
    {
        WithProjectTree(tree =>
        {
            var server = tree.AddProject("Shared/Shared.csproj", side: "Shared");
            var client = tree.AddProject("Client/Client.csproj", side: "Client");
            var engine = new FakeBuildEngine();
            var task = new ValidateKarpikProjectReferencesTask
            {
                BuildEngine = engine,
                ProjectPath = Path.Combine(tree.RootPath, client),
                ProjectReferences = [new TaskItem(Path.Combine("..", server))]
            };

            Assert.False(task.Execute());
            Assert.Equal(KarpikDiagnosticCodes.InvalidSolutionProject, Assert.Single(engine.Errors).Code);
        });
    }

    [Fact]
    public void DirectBuildTaskRejectsRawReferencesAbsentFromEvaluatedProject()
    {
        WithProjectTree(tree =>
        {
            var shared = tree.AddProject("Shared/Shared.csproj", side: "Shared");
            var client = tree.AddProject(
                "Client/Client.csproj",
                side: "Client",
                references: [shared],
                referenceCondition: "'$(IncludeShared)' == 'true'");
            var engine = new FakeBuildEngine();
            var task = new ValidateKarpikProjectReferencesTask
            {
                BuildEngine = engine,
                ProjectPath = Path.Combine(tree.RootPath, client),
                ProjectReferences = []
            };

            Assert.False(task.Execute());
            Assert.Equal(KarpikDiagnosticCodes.InvalidSolutionProject, Assert.Single(engine.Errors).Code);
        });
    }

    [Fact]
    public void DirectBuildTaskRejectsConditionalReferenceEvenWhenEvaluated()
    {
        WithProjectTree(tree =>
        {
            var shared = tree.AddProject("Shared/Shared.csproj", side: "Shared");
            var client = tree.AddProject(
                "Client/Client.csproj",
                side: "Client",
                references: [shared],
                referenceCondition: "'true' == 'true'");
            var engine = new FakeBuildEngine();
            var task = new ValidateKarpikProjectReferencesTask
            {
                BuildEngine = engine,
                ProjectPath = Path.Combine(tree.RootPath, client),
                ProjectReferences = [new TaskItem(Path.Combine(tree.RootPath, shared))]
            };

            Assert.False(task.Execute());
            Assert.Equal(KarpikDiagnosticCodes.InvalidSolutionProject, Assert.Single(engine.Errors).Code);
        });
    }

    [Fact]
    public void DirectBuildTaskReportsLiteralRawCycle()
    {
        WithProjectTree(tree =>
        {
            const string client = "Client/Client.csproj";
            var shared = tree.AddProject("Shared/Shared.csproj", side: "Shared", references: [client]);
            tree.AddProject(client, side: "Shared", references: [shared]);
            var engine = new FakeBuildEngine();
            var task = new ValidateKarpikProjectReferencesTask
            {
                BuildEngine = engine,
                ProjectPath = Path.Combine(tree.RootPath, client),
                ProjectReferences = [new TaskItem(Path.Combine(tree.RootPath, shared))]
            };

            Assert.False(task.Execute());
            Assert.Equal(KarpikDiagnosticCodes.ProjectReferenceCycle, Assert.Single(engine.Errors).Code);
        });
    }

    [Fact]
    public void DirectBuildTaskAcceptsValidEvaluatedReferences()
    {
        WithProjectTree(tree =>
        {
            var shared = tree.AddProject("Shared/Shared.csproj", side: "Shared");
            var client = tree.AddProject("Client/Client.csproj", side: "Client", references: [shared]);
            var engine = new FakeBuildEngine();
            var task = new ValidateKarpikProjectReferencesTask
            {
                BuildEngine = engine,
                ProjectPath = Path.Combine(tree.RootPath, client),
                ProjectReferences = [new TaskItem(Path.Combine(tree.RootPath, shared))]
            };

            Assert.True(task.Execute());
            Assert.Empty(engine.Errors);
        });
    }

    [Fact]
    public void DirectBuildTaskDeduplicatesEvaluatedReferences()
    {
        WithProjectTree(tree =>
        {
            var server = tree.AddProject("Server/Server.csproj", side: "Server");
            var serverPath = Path.Combine(tree.RootPath, server);
            var client = tree.AddProject("Client/Client.csproj", side: "Client", references: [server]);
            var engine = new FakeBuildEngine();
            var task = new ValidateKarpikProjectReferencesTask
            {
                BuildEngine = engine,
                ProjectPath = Path.Combine(tree.RootPath, client),
                ProjectReferences = [new TaskItem(serverPath), new TaskItem(serverPath)]
            };

            Assert.False(task.Execute());
            Assert.Equal(KarpikDiagnosticCodes.ForbiddenSideDependency, Assert.Single(engine.Errors).Code);
        });
    }

    [Fact]
    public void DirectBuildTaskReportsMissingEvaluatedReferenceOnce()
    {
        WithProjectTree(tree =>
        {
            var client = tree.AddProject("Client/Client.csproj", side: "Client", references: ["Missing/Missing.csproj"]);
            var engine = new FakeBuildEngine();
            var task = new ValidateKarpikProjectReferencesTask
            {
                BuildEngine = engine,
                ProjectPath = Path.Combine(tree.RootPath, client),
                ProjectReferences = [new TaskItem("../Missing/Missing.csproj")]
            };

            Assert.False(task.Execute());
            Assert.Equal(KarpikDiagnosticCodes.InvalidSolutionProject, Assert.Single(engine.Errors).Code);
        });
    }

    [Fact]
    public void DirectBuildTaskReportsForeignEvaluatedReferenceDiagnosticsOnceEach()
    {
        WithProjectTree(tree =>
        {
            var foreign = tree.AddProject("Foreign/Foreign.csproj", sdk: "Microsoft.NET.Sdk", kind: null, side: null);
            var client = tree.AddProject("Client/Client.csproj", side: "Client", references: [foreign]);
            var engine = new FakeBuildEngine();
            var task = new ValidateKarpikProjectReferencesTask
            {
                BuildEngine = engine,
                ProjectPath = Path.Combine(tree.RootPath, client),
                ProjectReferences = [new TaskItem(Path.Combine(tree.RootPath, foreign))]
            };

            Assert.False(task.Execute());
            Assert.Equal(
                [KarpikDiagnosticCodes.MissingSdk, KarpikDiagnosticCodes.InvalidProjectKind, KarpikDiagnosticCodes.InvalidProjectSide],
                engine.Errors.Select(error => error.Code));
        });
    }

    [Fact]
    public void DirectBuildTaskReportsOutsideRootEvaluatedReferenceOnce()
    {
        WithProjectTree(tree =>
        {
            tree.WriteGlobalJson();
            var outside = tree.AddProject("../Outside/Outside.csproj", side: "Server");
            var client = tree.AddProject("Client/Client.csproj", side: "Client", references: [outside]);
            var engine = new FakeBuildEngine();
            var task = new ValidateKarpikProjectReferencesTask
            {
                BuildEngine = engine,
                ProjectPath = Path.Combine(tree.RootPath, client),
                ProjectReferences = [new TaskItem(Path.Combine(tree.RootPath, outside))]
            };

            Assert.False(task.Execute());
            Assert.Equal(KarpikDiagnosticCodes.InvalidSolutionProject, Assert.Single(engine.Errors).Code);
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
        var container = Path.Combine(Path.GetTempPath(), "KarpikSdkTaskTests", Guid.NewGuid().ToString("N"));
        var root = Path.Combine(container, "Game");
        Directory.CreateDirectory(root);
        try
        {
            test(new TestProjectTree(root));
        }
        finally
        {
            Directory.Delete(container, recursive: true);
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
            IReadOnlyList<string>? references = null,
            string? referenceCondition = null)
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
                {
                    var projectReference = new XElement("ProjectReference", new XAttribute(
                        "Include",
                        Path.GetRelativePath(Path.GetDirectoryName(relativePath)!, reference)));
                    if (referenceCondition != null)
                    {
                        projectReference.Add(new XAttribute("Condition", referenceCondition));
                    }
                    return projectReference;
                })));
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

        public void WriteGlobalJson()
        {
            File.WriteAllText(Path.Combine(RootPath, "global.json"), "{}");
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
