namespace Karpik.Engine.Tooling;

public static class ModuleLayoutPolicy
{
    public static StringComparer ModuleIdComparer { get; } = StringComparer.OrdinalIgnoreCase;

    public static bool IsSafeModuleId(string? moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId) ||
            !char.IsAsciiLetterOrDigit(moduleId[0]) ||
            !char.IsAsciiLetterOrDigit(moduleId[^1]))
        {
            return false;
        }

        foreach (char character in moduleId)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_')
            {
                return false;
            }
        }

        return true;
    }

    public static string GetPrimaryAssemblyFileName(string moduleId)
    {
        if (!IsSafeModuleId(moduleId))
        {
            throw new ArgumentException("Module ID must be a portable safe path segment.", nameof(moduleId));
        }

        return moduleId + ".dll";
    }
}
