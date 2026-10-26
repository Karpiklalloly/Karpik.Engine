using ProjectConfigurator;
using System.Xml.Linq;
using Xunit;

public sealed class ModuleCodegenTests
{
    [Fact]
    public void Catalog_UsesRootReferenceRedirectForTransitiveInfrastructureOwner()
    {
        using var repository = new TestRepository();
        string owner = repository.AddLibrary("Infrastructure");
        repository.AddLibrary("Infrastructure.Codegen");
        string bridge = repository.AddLibrary("Bridge");
        string consumer = repository.AddPlugin(ProjectSide.Shared, "Consumer", directReference: bridge);
        repository.Select("Consumer");
        repository.Load();
        var project = XDocument.Load(Path.Combine(repository.RootPath, bridge));
        project.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "../../old/Infrastructure.csproj"))));
        project.Save(Path.Combine(repository.RootPath, bridge));
        File.WriteAllText(Path.Combine(repository.RootPath, "Directory.Build.targets"), $$"""
            <Project>
              <ItemGroup Condition="'$(MSBuildProjectName)' == 'Bridge'">
                <ProjectReference Remove="../../old/Infrastructure.csproj" />
                <ProjectReference Include="$(KarpikRepositoryRoot){{owner}}" />
              </ItemGroup>
            </Project>
            """);
        var model = RepositoryParser.Load(repository.RootPath, Path.Combine(repository.RootPath, "Test.slnx"));
        var graph = GraphValidator.Validate(model);
        Assert.True(graph.IsValid, string.Join("\n", graph.Errors));
        var catalog = XDocument.Parse(ArtifactGenerator.BuildArtifacts(model, graph)
            .Single(pair => pair.Key.EndsWith("KarpikModuleCatalog.props", StringComparison.Ordinal)).Value);
        Assert.Contains(catalog.Descendants("ProjectReference"), reference =>
            reference.Parent!.Attribute("Condition")!.Value.Contains(consumer, StringComparison.Ordinal)
            && reference.Attribute("Include")!.Value.EndsWith("Infrastructure.Codegen.csproj", StringComparison.Ordinal));
        string bridgePath = Path.GetFullPath(Path.Combine(repository.RootPath, bridge));
        Assert.Single(model.ProjectsByPath[bridgePath].DirectProjectReferences);
        Assert.Equal(Path.GetFullPath(Path.Combine(repository.RootPath, owner)),
            model.ProjectsByPath[bridgePath].DirectProjectReferences.Single());
    }

    [Fact]
    public void DisabledOptionalOwnerDoesNotCreateGeneratorCycle()
    {
        using var repository = new TestRepository();
        repository.AddPlugin(ProjectSide.Shared, "Provider");
        repository.AddPlugin(ProjectSide.Shared, "Consumer", dependencyIds: [("Provider", true)]);
        repository.AddPlugin(ProjectSide.Shared, "Provider.Codegen", dependencyIds: [("Consumer", false)]);
        repository.Select("Provider", false);
        repository.Select("Consumer");
        var graph = GraphValidator.Validate(repository.Load());
        Assert.True(graph.IsValid, string.Join("\n", graph.Errors));
    }

    [Fact]
    public void Validator_RejectsCycleAcrossTwoInferredGenerators()
    {
        using var repository = new TestRepository();
        repository.AddPlugin(ProjectSide.Shared, "A");
        repository.AddPlugin(ProjectSide.Shared, "B");
        string helperA = repository.AddPlugin(ProjectSide.Shared, "HelperA", dependencyIds: [("A", false)]);
        string helperB = repository.AddPlugin(ProjectSide.Shared, "HelperB", dependencyIds: [("B", false)]);
        repository.AddPlugin(ProjectSide.Shared, "A.Codegen", directReference: helperB);
        repository.AddPlugin(ProjectSide.Shared, "B.Codegen", directReference: helperA);
        foreach (string id in new[] { "A", "B", "HelperA", "HelperB" }) repository.Select(id);
        var graph = GraphValidator.Validate(repository.Load());
        Assert.Contains(graph.Errors, error => error.Contains("Codegen build cycle", StringComparison.Ordinal)
            && error.Contains("A.Codegen", StringComparison.Ordinal) && error.Contains("B.Codegen", StringComparison.Ordinal));
    }

    [Fact]
    public void GeneratorMayReferenceOwnerWhenOwnerDoesNotConsumeGenerator()
    {
        using var repository = new TestRepository();
        string owner = repository.AddPlugin(ProjectSide.Shared, "Provider");
        repository.AddPlugin(ProjectSide.Shared, "Provider.Codegen", directReference: owner);
        repository.Select("Provider");
        var graph = GraphValidator.Validate(repository.Load());
        Assert.True(graph.IsValid, string.Join("\n", graph.Errors));
    }

    [Fact]
    public void Validator_RejectsAmbiguousGeneratorNames()
    {
        using var repository = new TestRepository();
        repository.AddPlugin(ProjectSide.Shared, "Provider");
        repository.AddPlugin(ProjectSide.Shared, "Provider.Codegen");
        repository.AddLibrary("Provider.Codegen");
        repository.Select("Provider");
        var graph = GraphValidator.Validate(repository.Load());
        Assert.Contains(graph.Errors, error => error.Contains("Ambiguous codegen", StringComparison.Ordinal));
    }

    [Fact]
    public void Catalog_PropagatesInfrastructureOwnersWithoutMakingThemModules()
    {
        using var repository = new TestRepository();
        string owner = repository.AddLibrary("Infrastructure");
        repository.AddLibrary("Infrastructure.Codegen");
        repository.AddPlugin(ProjectSide.Shared, "Consumer", directReference: owner);
        repository.Select("Consumer");
        var model = repository.Load();
        var graph = GraphValidator.Validate(model);
        Assert.True(graph.IsValid, string.Join("\n", graph.Errors));
        Assert.Single(model.Plugins);
        string manifest = ArtifactGenerator.BuildCodegenCatalog(model);
        Assert.Contains("Infrastructure.Codegen.dll", manifest);
        Assert.DoesNotContain(repository.RootPath, manifest);
        Assert.Contains("\"ModuleId\": null", manifest);
        string catalog = ArtifactGenerator.BuildArtifacts(model, graph)
            .Single(pair => pair.Key.EndsWith("KarpikModuleCatalog.props", StringComparison.Ordinal)).Value;
        Assert.Contains("Infrastructure.Codegen.csproj", catalog);
    }

    [Fact]
    public void Parser_ExcludesCodegenProjectsFromRuntimeModules()
    {
        using var repository = new TestRepository();
        repository.AddPlugin(ProjectSide.Shared, "Provider.Core");
        repository.AddPlugin(ProjectSide.Shared, "Provider.Core.Codegen");
        repository.Select("Provider");
        var model = repository.Load();
        Assert.Single(model.Plugins);
        Assert.True(GraphValidator.Validate(model).IsValid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Catalog_PropagatesCodegenTransitivelyWithoutDuplicates(bool enabled)
    {
        using var repository = new TestRepository();
        repository.AddPlugin(ProjectSide.Shared, "Provider.Core");
        string generator = repository.AddLibrary("Provider.Core.Codegen");
        repository.AddPlugin(ProjectSide.Shared, "Left", dependencyIds: [("Provider", true)]);
        repository.AddPlugin(ProjectSide.Shared, "Right", dependencyIds: [("Provider", true)]);
        string consumer = repository.AddPlugin(ProjectSide.Shared, "Consumer", dependencyIds: [("Left", false), ("Right", false)]);
        repository.Select("Provider", enabled);
        repository.Select("Left");
        repository.Select("Right");
        repository.Select("Consumer");
        var model = repository.Load();
        var graph = GraphValidator.Validate(model);
        Assert.True(graph.IsValid, string.Join("\n", graph.Errors));
        var catalog = XDocument.Parse(ArtifactGenerator.BuildArtifacts(model, graph)
            .Single(pair => pair.Key.EndsWith("KarpikModuleCatalog.props", StringComparison.Ordinal)).Value);
        var references = catalog.Descendants("ProjectReference")
            .Where(reference => reference.Parent!.Attribute("Condition")!.Value.Contains(consumer, StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(enabled ? 1 : 0, references.Length);
        if (enabled)
        {
            Assert.EndsWith(generator.Replace('/', '\\'), (string?)references[0].Attribute("Include"));
            Assert.Equal("Analyzer", (string?)references[0].Attribute("OutputItemType"));
            Assert.Equal("false", (string?)references[0].Attribute("ReferenceOutputAssembly"));
        }
    }

    [Fact]
    public void Validator_RejectsGeneratorDependingOnItsConsumer()
    {
        using var repository = new TestRepository();
        repository.AddPlugin(ProjectSide.Shared, "Provider");
        repository.AddPlugin(ProjectSide.Shared, "Consumer", dependencyIds: [("Provider", false)]);
        repository.AddPlugin(ProjectSide.Shared, "Provider.Codegen", dependencyIds: [("Consumer", false)]);
        repository.Select("Provider");
        repository.Select("Consumer");
        var graph = GraphValidator.Validate(repository.Load());
        Assert.Contains(graph.Errors, error => error.Contains("codegen build cycle", StringComparison.OrdinalIgnoreCase));
    }
}
