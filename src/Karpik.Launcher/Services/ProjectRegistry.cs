using System.Text.Json;
using Karpik.Launcher.Models;

namespace Karpik.Launcher.Services;

public sealed class ProjectRegistry
{
    private const int MaximumRecentProjects = 20;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public ProjectRegistry(string? localApplicationDataRoot = null)
    {
        string localRoot = Path.GetFullPath(localApplicationDataRoot ??
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        RegistryPath = Path.Combine(localRoot, "Karpik", "Launcher", "recent-projects.json");
    }

    public string RegistryPath { get; }
    public string? LastLoadDiagnostic { get; private set; }

    public IReadOnlyList<RecentProject> Load()
    {
        LastLoadDiagnostic = null;
        if (!File.Exists(RegistryPath))
        {
            return [];
        }

        try
        {
            RecentProject?[] projects = JsonSerializer.Deserialize<RecentProject?[]>(
                File.ReadAllText(RegistryPath),
                _jsonOptions) ?? [];
            if (projects.Any(project => project is null))
            {
                LastLoadDiagnostic = "The recent-project registry contains invalid null entries.";
            }
            return projects
                .OfType<RecentProject>()
                .Where(project => IsValidSolutionPath(project.SolutionPath))
                .GroupBy(project => Path.GetFullPath(project.SolutionPath), PathComparer)
                .Select(group => group.OrderByDescending(project => project.LastOpenedUtc).First())
                .OrderByDescending(project => project.LastOpenedUtc)
                .Take(MaximumRecentProjects)
                .ToArray();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            LastLoadDiagnostic = $"Cannot read the recent-project registry: {exception.Message}";
            return [];
        }
    }

    public void Add(string solutionPath, DateTime? openedAtUtc = null)
    {
        string path = NormalizeSolutionPath(solutionPath);
        var recent = new RecentProject(path, openedAtUtc ?? DateTime.UtcNow);
        RecentProject[] projects = Load()
            .Where(project => !PathComparer.Equals(project.SolutionPath, path))
            .Prepend(recent)
            .Take(MaximumRecentProjects)
            .ToArray();

        Save(projects);
    }

    public void Remove(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        string path = Path.GetFullPath(solutionPath);
        Save(Load().Where(project => !PathComparer.Equals(project.SolutionPath, path)).ToArray());
    }

    private void Save(IReadOnlyList<RecentProject> projects)
    {
        string directory = Path.GetDirectoryName(RegistryPath)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(RegistryPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(projects, _jsonOptions));
            File.Move(temporaryPath, RegistryPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string NormalizeSolutionPath(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        if (!Path.IsPathFullyQualified(solutionPath))
        {
            throw new ArgumentException("The recent project path must be absolute.", nameof(solutionPath));
        }
        string path = Path.GetFullPath(solutionPath);
        if (!IsValidSolutionPath(path))
        {
            throw new FileNotFoundException("The recent project must be an existing .slnx file.", path);
        }
        return path;
    }

    private static bool IsValidSolutionPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Path.IsPathFullyQualified(path) &&
        string.Equals(Path.GetExtension(path), ".slnx", StringComparison.OrdinalIgnoreCase) &&
        File.Exists(path);

    private static StringComparer PathComparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
