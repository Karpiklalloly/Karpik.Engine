using System.Text.Json;

namespace Karpik.Editor;

public enum EditorUiDensity { Compact, UltraCompact, Large }

public enum EditorLayoutPreset { Unity, Debug, Custom }

public sealed class EditorWorkspace
{
    public string? SolutionPath { get; init; }
    public EditorUiDensity UiDensity { get; init; } = EditorUiDensity.Compact;
    public EditorLayoutPreset LayoutPreset { get; init; } = EditorLayoutPreset.Unity;
    public double LeftPanelWidth { get; init; } = 300;
    public double BottomPanelHeight { get; init; } = 220;
    public double WindowWidth { get; init; } = 1400;
    public double WindowHeight { get; init; } = 900;
}

public sealed class WorkspaceStore
{
    private readonly string _path;

    public WorkspaceStore(string path)
    {
        _path = path;
    }

    public async Task<EditorWorkspace> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new EditorWorkspace();
        }

        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<EditorWorkspace>(stream, cancellationToken: cancellationToken)
               ?? new EditorWorkspace();
    }

    public async Task SaveAsync(EditorWorkspace workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = _path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, workspace, cancellationToken: cancellationToken);
        }

        File.Move(temporaryPath, _path, overwrite: true);
    }

    public static WorkspaceStore CreateDefault()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new WorkspaceStore(Path.Combine(root, "KarpikEngine", "Editor", "workspace.json"));
    }
}
