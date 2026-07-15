using System.Xml.Linq;
using Karpik.Engine.ProjectModel;
using Xunit;

public sealed class GameSolutionValidationTests
{
    [Fact]
    public void RuntimeClientMayReferenceRuntimeShared()
    {
        WithSolution(solution =>
        {
            var shared = solution.AddProject("Shared/Shared.csproj", side: "Shared");
            solution.AddProject("Client/Client.csproj", side: "Client", references: [shared]);

            AssertValid(solution);
        });
    }

    [Theory]
    [InlineData("Client", "Server")]
    [InlineData("Server", "Client")]
    public void RuntimeSidesMayNotReferenceTheOppositeSide(string sourceSide, string targetSide)
    {
        WithSolution(solution =>
        {
            var target = solution.AddProject("Target/Target.csproj", side: targetSide);
            solution.AddProject("Source/Source.csproj", side: sourceSide, references: [target]);

            AssertCodes(solution, KarpikDiagnosticCodes.ForbiddenSideDependency);
        });
    }

    [Theory]
    [InlineData("Client")]
    [InlineData("Server")]
    public void SharedMayNotReferenceRuntimeSpecificProjects(string targetSide)
    {
        WithSolution(solution =>
        {
            var target = solution.AddProject("Target/Target.csproj", side: targetSide);
            solution.AddProject("Shared/Shared.csproj", side: "Shared", references: [target]);

            AssertCodes(solution, KarpikDiagnosticCodes.ForbiddenSideDependency);
        });
    }

    [Fact]
    public void EveryProjectMustUseTheKarpikSdk()
    {
        WithSolution(solution =>
        {
            solution.AddProject("Client/Client.csproj", sdk: "Microsoft.NET.Sdk", side: "Client");

            AssertCodes(solution, KarpikDiagnosticCodes.MissingSdk);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Unknown")]
    public void ProjectKindIsRequiredAndMustBeValid(string? kind)
    {
        WithSolution(solution =>
        {
            solution.AddProject("Client/Client.csproj", kind: kind, side: "Client");

            AssertCodes(solution, KarpikDiagnosticCodes.InvalidProjectKind);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Unknown")]
    public void ProjectSideIsRequiredAndMustBeValid(string? side)
    {
        WithSolution(solution =>
        {
            solution.AddProject("Client/Client.csproj", side: side);

            AssertCodes(solution, KarpikDiagnosticCodes.InvalidProjectSide);
        });
    }

    [Fact]
    public void RuntimeProjectMayNotUseNoneSide()
    {
        WithSolution(solution =>
        {
            solution.AddProject("Runtime/Runtime.csproj", side: "None");

            AssertCodes(solution, KarpikDiagnosticCodes.InvalidProjectSide);
        });
    }

    [Fact]
    public void MissingSolutionProjectProducesStructuralDiagnosticOnly()
    {
        WithSolution(solution =>
        {
            solution.AddSolutionEntry("Missing/Missing.csproj");

            AssertCodes(solution, KarpikDiagnosticCodes.InvalidSolutionProject);
        });
    }

    [Fact]
    public void DuplicateSolutionProjectProducesStructuralDiagnostic()
    {
        WithSolution(solution =>
        {
            var project = solution.AddProject("Shared/Shared.csproj", side: "Shared");
            solution.AddSolutionEntry(project);

            AssertCodes(solution, KarpikDiagnosticCodes.InvalidSolutionProject);
        });
    }

    [Fact]
    public void OutsideRootSolutionProjectProducesStructuralDiagnosticOnly()
    {
        WithSolution(solution =>
        {
            solution.AddProject("../Outside/Outside.csproj", side: "Shared");

            AssertCodes(solution, KarpikDiagnosticCodes.InvalidSolutionProject);
        });
    }

    [Fact]
    public void UnreadableSolutionProjectProducesStructuralDiagnosticOnly()
    {
        WithSolution(solution =>
        {
            var project = solution.AddProject("Broken/Broken.csproj", side: "Shared");
            File.WriteAllText(Path.Combine(solution.RootPath, project), "<Project>");

            AssertCodes(solution, KarpikDiagnosticCodes.InvalidSolutionProject);
        });
    }

    [Fact]
    public void ProjectReferenceToProjectOutsideTheSolutionProducesStructuralDiagnostic()
    {
        WithSolution(solution =>
        {
            var omitted = solution.AddProject("Omitted/Omitted.csproj", side: "Shared", includeInSolution: false);
            solution.AddProject("Shared/Shared.csproj", side: "Shared", references: [omitted]);

            AssertCodes(solution, KarpikDiagnosticCodes.InvalidSolutionProject);
        });
    }

    [Fact]
    public void InvalidProjectReferencePathProducesStructuralDiagnosticOnly()
    {
        WithSolution(solution =>
        {
            var project = solution.AddProject("Shared/Shared.csproj", side: "Shared");
            var projectPath = Path.Combine(solution.RootPath, project);
            var document = XDocument.Load(projectPath);
            document.Root!.Add(new XElement("ItemGroup",
                new XElement("ProjectReference", new XAttribute("Include", "<>"))));
            document.Save(projectPath);

            AssertCodes(solution, KarpikDiagnosticCodes.InvalidSolutionProject);
        });
    }

    [Fact]
    public void ProjectReferenceCycleIsRejected()
    {
        WithSolution(solution =>
        {
            solution.AddProject("Shared/A/A.csproj", side: "Shared", references: ["Shared/B/B.csproj"]);
            solution.AddProject("Shared/B/B.csproj", side: "Shared", references: ["Shared/A/A.csproj"]);

            AssertCodes(solution, KarpikDiagnosticCodes.ProjectReferenceCycle);
        });
    }

    [Fact]
    public void TestProjectWithExplicitSideIsValid()
    {
        WithSolution(solution =>
        {
            var shared = solution.AddProject("Shared/Shared.csproj", side: "Shared");
            solution.AddProject("Tests/Shared.Tests.csproj", kind: "Test", side: "Shared", references: [shared]);

            AssertValid(solution);
        });
    }

    [Fact]
    public void TestProjectMayNotUseNoneSide()
    {
        WithSolution(solution =>
        {
            solution.AddProject("Tests/Tool.Tests.csproj", kind: "Test", side: "None");

            AssertCodes(solution, KarpikDiagnosticCodes.InvalidProjectSide);
        });
    }

    [Theory]
    [InlineData("Tool")]
    [InlineData("Generator")]
    [InlineData("Assets")]
    public void NonRuntimeProjectKindsAcceptNoneSide(string kind)
    {
        WithSolution(solution =>
        {
            solution.AddProject($"{kind}/{kind}.csproj", kind: kind, side: "None");

            AssertValid(solution);
        });
    }

    [Fact]
    public void NoneSideDoesNotGainRuntimeAccess()
    {
        WithSolution(solution =>
        {
            var shared = solution.AddProject("Shared/Shared.csproj", side: "Shared");
            solution.AddProject("Tool/Tool.csproj", kind: "Tool", side: "None", references: [shared]);

            AssertCodes(solution, KarpikDiagnosticCodes.ForbiddenSideDependency);
        });
    }

    [Fact]
    public void ReaderParsesSdkVersionSdkDeclarationsAndModuleMetadata()
    {
        WithSolution(solution =>
        {
            solution.WriteGlobalJson("0.6.0-local");
            solution.AddProject(
                "Client/Client.csproj",
                sdk: "Microsoft.NET.Sdk;Karpik.Engine.Sdk/0.6.0-local",
                side: "Client",
                modules: [("Graphics", "OpenGL", false), ("LoggerModule", null, true)]);

            var model = new KarpikSolutionReader().Read(solution.WriteSolution());
            var project = Assert.Single(model.Projects);

            Assert.Equal("0.6.0-local", model.SdkVersion);
            Assert.Contains("Karpik.Engine.Sdk", project.SdkNames);
            Assert.Equal(
                [new KarpikModuleReference("Graphics", "OpenGL", false), new KarpikModuleReference("LoggerModule", null, true)],
                project.Modules);
        });
    }

    [Fact]
    public void AllStableDiagnosticCodesAreDefined()
    {
        Assert.Equal("KARPIK001", KarpikDiagnosticCodes.MissingSdk);
        Assert.Equal("KARPIK002", KarpikDiagnosticCodes.InvalidProjectKind);
        Assert.Equal("KARPIK003", KarpikDiagnosticCodes.InvalidProjectSide);
        Assert.Equal("KARPIK004", KarpikDiagnosticCodes.InvalidSolutionProject);
        Assert.Equal("KARPIK005", KarpikDiagnosticCodes.ForbiddenSideDependency);
        Assert.Equal("KARPIK006", KarpikDiagnosticCodes.ProjectReferenceCycle);
        Assert.Equal("KARPIK007", KarpikDiagnosticCodes.UnknownOrAmbiguousModule);
        Assert.Equal("KARPIK008", KarpikDiagnosticCodes.MissingRequiredModule);
    }

    private static void AssertValid(TestSolution solution)
    {
        Assert.Empty(ReadAndValidate(solution));
    }

    private static void AssertCodes(TestSolution solution, params string[] expectedCodes)
    {
        var diagnostics = ReadAndValidate(solution);
        Assert.Equal(expectedCodes, diagnostics.Select(diagnostic => diagnostic.Code));
    }

    private static IReadOnlyList<KarpikDiagnostic> ReadAndValidate(TestSolution solution)
    {
        var model = new KarpikSolutionReader().Read(solution.WriteSolution());
        return new KarpikSolutionValidator().Validate(model);
    }

    private static void WithSolution(Action<TestSolution> test)
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "KarpikProjectModelTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            test(new TestSolution(Path.Combine(temporaryRoot, "Game")));
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }

    private sealed class TestSolution
    {
        private readonly List<string> _solutionEntries = [];

        public TestSolution(string rootPath)
        {
            RootPath = rootPath;
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string AddProject(
            string relativePath,
            string? sdk = "Karpik.Engine.Sdk",
            string? kind = "Runtime",
            string? side = "Shared",
            IReadOnlyList<string>? references = null,
            IReadOnlyList<(string Id, string? Implementation, bool Optional)>? modules = null,
            bool includeInSolution = true)
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
            if (modules is { Count: > 0 })
            {
                project.Add(new XElement("ItemGroup", modules.Select(module =>
                {
                    var dependency = new XElement("KarpikModuleDependency", new XAttribute("Include", module.Id));
                    if (module.Implementation != null)
                    {
                        dependency.Add(new XAttribute("Implementation", module.Implementation));
                    }
                    if (module.Optional)
                    {
                        dependency.Add(new XAttribute("Optional", "true"));
                    }
                    return dependency;
                })));
            }

            var absolutePath = Path.GetFullPath(Path.Combine(RootPath, relativePath));
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            new XDocument(project).Save(absolutePath);
            if (includeInSolution)
            {
                _solutionEntries.Add(relativePath);
            }
            return relativePath;
        }

        public void AddSolutionEntry(string relativePath)
        {
            _solutionEntries.Add(relativePath);
        }

        public string WriteSolution()
        {
            var path = Path.Combine(RootPath, "Game.slnx");
            new XDocument(new XElement("Solution", _solutionEntries.Select(entry =>
                new XElement("Project", new XAttribute("Path", entry))))).Save(path);
            return path;
        }

        public void WriteGlobalJson(string sdkVersion)
        {
            File.WriteAllText(Path.Combine(RootPath, "global.json"), $$"""
                {
                  "msbuild-sdks": {
                    "Karpik.Engine.Sdk": "{{sdkVersion}}"
                  }
                }
                """);
        }
    }
}
