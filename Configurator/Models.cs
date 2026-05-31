namespace ProjectConfigurator;

public enum ProjectSide
{
    Unknown,
    Shared,
    Client,
    Server
}

public enum PluginKind
{
    Standalone,
    Core,
    Implementation
}

public sealed record DependencySpec(string Id, string? TargetPath, bool Optional);

public sealed record SettingSchema(
    string ModuleId,
    string Name,
    string Type,
    string? DefaultValue,
    bool Required,
    IReadOnlyList<string> AllowedValues);

public sealed class ProjectInfo
{
    public required string AbsolutePath { get; init; }
    public required string RelativePath { get; init; }
    public required string PluginId { get; init; }
    public required ProjectSide Side { get; init; }
    public required bool IsExecutable { get; init; }
    public required bool IsTestProject { get; init; }
    public required string? AssemblyName { get; init; }
    public List<DependencySpec> Dependencies { get; } = [];
    public List<string> DirectProjectReferences { get; } = [];
    public List<SettingSchema> Settings { get; } = [];
}

public sealed class PluginInfo
{
    public required ProjectInfo Project { get; init; }
    public required string ModuleId { get; init; }
    public required PluginKind Kind { get; init; }
    public string? ImplementationId { get; init; }
}

public sealed class ModuleSelection
{
    public required string ModuleId { get; init; }
    public bool Enabled { get; set; }
    public string? Implementation { get; set; }
}

public sealed class ModuleInfo
{
    public required string Id { get; init; }
    public PluginInfo? Standalone { get; set; }
    public PluginInfo? Core { get; set; }
    public SortedDictionary<string, PluginInfo> Implementations { get; } = new(StringComparer.Ordinal);
}

public sealed class RepositoryModel
{
    public required string RootPath { get; init; }
    public required string SolutionPath { get; init; }
    public required string PropsPath { get; init; }
    public required IReadOnlyDictionary<string, ProjectInfo> ProjectsByPath { get; init; }
    public required IReadOnlyList<PluginInfo> Plugins { get; init; }
    public required IReadOnlyDictionary<string, ModuleInfo> Modules { get; init; }
    public required IReadOnlyList<ProjectInfo> GameRoots { get; init; }
    public required IReadOnlyDictionary<string, ModuleSelection> Selections { get; init; }
    public required IReadOnlyDictionary<string, string> SettingValues { get; init; }
    public required IReadOnlyList<string> ParseErrors { get; init; }
}

public sealed class GraphResult
{
    public List<string> Errors { get; } = [];
    public List<PluginInfo> ActivePlugins { get; } = [];
    public List<ProjectInfo> ClientLoadOrder { get; } = [];
    public List<ProjectInfo> ServerLoadOrder { get; } = [];
    public bool IsValid => Errors.Count == 0;
}
