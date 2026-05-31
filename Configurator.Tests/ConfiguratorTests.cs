using System.Xml.Linq;
using ProjectConfigurator;
using Xunit;

public sealed class ConfiguratorTests
{
    [Fact]
    public void Parser_InfersPluginKindsAndExcludesTestsAndExecutables()
    {
        using var repository = new TestRepository();
        repository.AddPlugin(ProjectSide.Client, "Input");
        repository.AddPlugin(ProjectSide.Client, "Graphics.Core");
        repository.AddPlugin(ProjectSide.Client, "Graphics.OpenGL");
        repository.AddPlugin(ProjectSide.Client, "Graphics.Core.Tests", isTest: true);
        repository.AddPlugin(ProjectSide.Shared, "Modding.TestModule", executable: true);
        repository.Select("Input");
        repository.Select("Graphics", implementation: "OpenGL");

        var model = repository.Load();

        Assert.Equal(2, model.Modules.Count);
        Assert.Equal(PluginKind.Standalone, model.Modules["Input"].Standalone!.Kind);
        Assert.Equal(PluginKind.Core, model.Modules["Graphics"].Core!.Kind);
        Assert.Equal("OpenGL", model.Modules["Graphics"].Implementations["OpenGL"].ImplementationId);
    }

    [Fact]
    public void Parser_ResolvesLogicalCoreDependencyId()
    {
        using var repository = new TestRepository();
        var physics = repository.AddPlugin(ProjectSide.Shared, "Physics2D.Core");
        repository.AddPlugin(ProjectSide.Shared, "Consumer", dependencies: [(physics, false)]);
        repository.Select("Physics2D");
        repository.Select("Consumer");

        var model = repository.Load();
        var graph = GraphValidator.Validate(model);

        Assert.True(graph.IsValid);
        Assert.Equal("Physics2D", model.Modules["Consumer"].Standalone!.Project.Dependencies.Single().Id);
    }

    [Fact]
    public void Parser_RejectsUnknownAndAmbiguousDependencyIds()
    {
        using var unknownRepository = new TestRepository();
        unknownRepository.AddPlugin(ProjectSide.Shared, "Consumer", dependencyIds: [("Missing", false)]);
        unknownRepository.Select("Consumer");
        Assert.Contains(GraphValidator.Validate(unknownRepository.Load()).Errors,
            error => error.Contains("unknown KarpikModuleDependency id 'Missing'", StringComparison.Ordinal));

        using var ambiguousRepository = new TestRepository();
        ambiguousRepository.AddPlugin(ProjectSide.Shared, "Collision");
        ambiguousRepository.AddPlugin(ProjectSide.Shared, "Collision.Core");
        ambiguousRepository.Select("Collision");
        Assert.Contains(GraphValidator.Validate(ambiguousRepository.Load()).Errors,
            error => error.Contains("Ambiguous project id 'Collision'", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsInvalidAssemblyNameAndDirectProjectReference()
    {
        using var repository = new TestRepository();
        var dependency = repository.AddPlugin(ProjectSide.Shared, "Dependency");
        repository.AddPlugin(ProjectSide.Shared, "Broken", assemblyName: "Wrong", directReference: dependency);
        repository.Select("Dependency");
        repository.Select("Broken");

        var graph = GraphValidator.Validate(repository.Load());

        Assert.Contains(graph.Errors, error => error.Contains("AssemblyName must match", StringComparison.Ordinal));
        Assert.Contains(graph.Errors, error => error.Contains("direct ProjectReference is forbidden", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsMissingSelection()
    {
        using var repository = new TestRepository();
        repository.AddPlugin(ProjectSide.Shared, "Unselected");

        var graph = GraphValidator.Validate(repository.Load());

        Assert.Contains(graph.Errors, error => error.Contains("Missing KarpikModuleSelection for module Unselected", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsRequiredDisabledDependencyButAllowsOptionalDisabledDependency()
    {
        using var requiredRepository = new TestRepository();
        var disabled = requiredRepository.AddPlugin(ProjectSide.Shared, "Disabled");
        requiredRepository.AddPlugin(ProjectSide.Shared, "Consumer", dependencies: [(disabled, false)]);
        requiredRepository.Select("Disabled", enabled: false);
        requiredRepository.Select("Consumer");
        Assert.Contains(GraphValidator.Validate(requiredRepository.Load()).Errors,
            error => error.Contains("required module dependency is disabled", StringComparison.Ordinal));

        using var optionalRepository = new TestRepository();
        disabled = optionalRepository.AddPlugin(ProjectSide.Shared, "Disabled");
        optionalRepository.AddPlugin(ProjectSide.Shared, "Consumer", dependencies: [(disabled, true)]);
        optionalRepository.Select("Disabled", enabled: false);
        optionalRepository.Select("Consumer");
        Assert.True(GraphValidator.Validate(optionalRepository.Load()).IsValid);
    }

    [Fact]
    public void Validator_RejectsSideLeaksAndCycles()
    {
        using var sideRepository = new TestRepository();
        var client = sideRepository.AddPlugin(ProjectSide.Client, "ClientOnly");
        sideRepository.AddPlugin(ProjectSide.Shared, "SharedBroken", dependencies: [(client, false)]);
        sideRepository.Select("ClientOnly");
        sideRepository.Select("SharedBroken");
        Assert.Contains(GraphValidator.Validate(sideRepository.Load()).Errors,
            error => error.Contains("references forbidden project", StringComparison.Ordinal));

        using var cycleRepository = new TestRepository();
        var a = cycleRepository.AddPlugin(ProjectSide.Shared, "A");
        var b = cycleRepository.AddPlugin(ProjectSide.Shared, "B", dependencies: [(a, false)]);
        cycleRepository.ReplaceDependencies(a, [(b, false)]);
        cycleRepository.Select("A");
        cycleRepository.Select("B");
        Assert.Contains(GraphValidator.Validate(cycleRepository.Load()).Errors,
            error => error.Contains("contains a cycle", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_ValidatesSettingSchemaValues()
    {
        using var repository = new TestRepository();
        repository.AddPlugin(ProjectSide.Shared, "Settings",
            settings: [("Count", "Int32", null, true, null), ("Mode", "Enum", "Fast", false, "Fast;Safe")]);
        repository.Select("Settings");
        repository.Set("Settings.Count", "not-an-int");

        var graph = GraphValidator.Validate(repository.Load());

        Assert.Contains(graph.Errors, error => error.Contains("Settings.Count has invalid Int32 value", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_IsDeterministicAndIncludesGameRootsInBothSides()
    {
        using var repository = new TestRepository();
        var provider = repository.AddPlugin(ProjectSide.Shared, "Provider");
        repository.AddPlugin(ProjectSide.Client, "Consumer", dependencies: [(provider, false)]);
        repository.Select("Provider");
        repository.Select("Consumer");
        var model = repository.Load();
        var graph = GraphValidator.Validate(model);

        var first = ArtifactGenerator.BuildArtifacts(model, graph);
        var second = ArtifactGenerator.BuildArtifacts(model, graph);
        var loader = first.Single(artifact => artifact.Key.EndsWith("ModuleLoader.cs", StringComparison.Ordinal)).Value;

        Assert.True(graph.IsValid);
        Assert.Equal(first.Values, second.Values);
        Assert.True(graph.ClientLoadOrder.FindIndex(project => project.PluginId == "Provider") <
                    graph.ClientLoadOrder.FindIndex(project => project.PluginId == "Consumer"));
        Assert.Contains("MyGame.Client.Main", loader);
        Assert.Contains("MyGame.Server.Main", loader);
        Assert.Contains("MyGameResources", loader);
    }
}

internal sealed class TestRepository : IDisposable
{
    private readonly List<string> _projects = [];
    private readonly Dictionary<string, XElement> _projectDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<XElement> _selections = [];
    private readonly List<XElement> _settings = [];

    public TestRepository()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "KarpikConfiguratorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
        AddGameRoot("MyGame/MyGameResources/MyGameResources.csproj");
        AddGameRoot("MyGame/Shared/MyGame.Shared.Main/MyGame.Shared.Main.csproj");
        AddGameRoot("MyGame/Client/MyGame.Client.Main/MyGame.Client.Main.csproj");
        AddGameRoot("MyGame/Server/MyGame.Server.Main/MyGame.Server.Main.csproj");
    }

    public string RootPath { get; }

    public string AddPlugin(
        ProjectSide side,
        string pluginId,
        bool isTest = false,
        bool executable = false,
        string? assemblyName = null,
        string? directReference = null,
        (string Target, bool Optional)[]? dependencies = null,
        (string Id, bool Optional)[]? dependencyIds = null,
        (string Name, string Type, string? Default, bool Required, string? Values)[]? settings = null)
    {
        var relativePath = $"Modules/{side}/{pluginId}/{pluginId}.csproj";
        var propertyGroup = new XElement("PropertyGroup",
            new XElement("TargetFramework", "net10.0"));
        if (isTest) propertyGroup.Add(new XElement("IsTestProject", "true"));
        if (executable) propertyGroup.Add(new XElement("OutputType", "Exe"));
        if (assemblyName != null) propertyGroup.Add(new XElement("AssemblyName", assemblyName));
        var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), propertyGroup);
        if (directReference != null)
        {
            project.Add(new XElement("ItemGroup",
                new XElement("ProjectReference", new XAttribute("Include", RelativeReference(relativePath, directReference)))));
        }
        AddDependencies(project, relativePath, dependencies);
        AddDependencyIds(project, dependencyIds);
        if (settings != null)
        {
            project.Add(new XElement("ItemGroup", settings.Select(setting =>
            {
                var element = new XElement("KarpikModuleSetting",
                    new XAttribute("Include", setting.Name),
                    new XAttribute("Type", setting.Type));
                if (setting.Default != null) element.Add(new XAttribute("Default", setting.Default));
                if (setting.Required) element.Add(new XAttribute("Required", "true"));
                if (setting.Values != null) element.Add(new XAttribute("Values", setting.Values));
                return element;
            })));
        }
        AddProject(relativePath, project);
        return relativePath;
    }

    public void ReplaceDependencies(string projectPath, (string Target, bool Optional)[] dependencies)
    {
        var project = _projectDocuments[projectPath];
        project.Elements("ItemGroup").Where(group => group.Elements("KarpikModuleDependency").Any()).Remove();
        AddDependencies(project, projectPath, dependencies);
    }

    public void Select(string moduleId, bool enabled = true, string? implementation = null)
    {
        var selection = new XElement("KarpikModuleSelection",
            new XAttribute("Include", moduleId),
            new XAttribute("Enabled", enabled.ToString().ToLowerInvariant()));
        if (implementation != null) selection.Add(new XAttribute("Implementation", implementation));
        _selections.Add(selection);
    }

    public void Set(string id, string value)
    {
        _settings.Add(new XElement("KarpikModuleSettingValue",
            new XAttribute("Include", id),
            new XAttribute("Value", value)));
    }

    public RepositoryModel Load()
    {
        foreach (var project in _projectDocuments)
        {
            var path = Path.Combine(RootPath, project.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            new XDocument(project.Value).Save(path);
        }
        new XDocument(new XElement("Solution", _projects.Select(path =>
            new XElement("Project", new XAttribute("Path", path))))).Save(Path.Combine(RootPath, "Test.slnx"));
        new XDocument(new XElement("Project",
            new XElement("ItemGroup", new XAttribute("Label", "ProjectConfigurator"), _selections.Concat(_settings))))
            .Save(Path.Combine(RootPath, "Directory.Build.props"));
        return RepositoryParser.Load(RootPath, Path.Combine(RootPath, "Test.slnx"));
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }

    private void AddGameRoot(string relativePath)
    {
        AddProject(relativePath, new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"))));
    }

    private void AddProject(string relativePath, XElement project)
    {
        _projects.Add(relativePath);
        _projectDocuments.Add(relativePath, project);
    }

    private static void AddDependencies(XElement project, string sourcePath, (string Target, bool Optional)[]? dependencies)
    {
        if (dependencies == null) return;
        AddDependencyIds(project, dependencies.Select(dependency =>
            (DependencyId(dependency.Target), dependency.Optional)).ToArray());
    }

    private static void AddDependencyIds(XElement project, (string Id, bool Optional)[]? dependencies)
    {
        if (dependencies == null) return;
        project.Add(new XElement("ItemGroup", dependencies.Select(dependency =>
        {
            var element = new XElement("KarpikModuleDependency",
                new XAttribute("Include", dependency.Id));
            if (dependency.Optional) element.Add(new XAttribute("Optional", "true"));
            return element;
        })));
    }

    private static string DependencyId(string targetPath)
    {
        var id = Path.GetFileNameWithoutExtension(targetPath);
        return targetPath.StartsWith("Modules/", StringComparison.Ordinal) &&
               id.EndsWith(".Core", StringComparison.Ordinal)
            ? id[..^".Core".Length]
            : id;
    }

    private static string RelativeReference(string sourcePath, string targetPath)
    {
        return Path.GetRelativePath(Path.GetDirectoryName(sourcePath)!, targetPath);
    }
}

internal static class ListExtensions
{
    public static int FindIndex<T>(this IReadOnlyList<T> items, Func<T, bool> predicate)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (predicate(items[index])) return index;
        }
        return -1;
    }
}
