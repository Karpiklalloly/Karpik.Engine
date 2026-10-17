namespace Karpik.Engine.ProjectModel;

/// <summary>Содержит единые правила нормализации и сравнения путей проекта.</summary>
internal static class KarpikPathPolicy
{
    /// <summary>Получает comparer путей, соответствующий текущей платформе.</summary>
    public static StringComparer Comparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <summary>Преобразует путь в абсолютный путь, при необходимости относительно базы.</summary>
    public static string Normalize(string path, string? basePath = null)
    {
        return basePath == null
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, basePath);
    }

    /// <summary>Проверяет, находится ли путь внутри указанного корня.</summary>
    public static bool IsWithinRoot(string path, string rootPath)
    {
        var relativePath = Path.GetRelativePath(rootPath, path);
        return !Path.IsPathRooted(relativePath) &&
               !relativePath.Equals("..", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }
}
