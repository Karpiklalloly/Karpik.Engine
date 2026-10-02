namespace Karpik.Engine.Tooling;

/// <summary>Определяет безопасные имена и layout-файлы модулей engine payload.</summary>
public static class ModuleLayoutPolicy
{
    /// <summary>Получает comparer идентификаторов модулей без учёта регистра.</summary>
    public static StringComparer ModuleIdComparer { get; } = StringComparer.OrdinalIgnoreCase;

    /// <summary>Проверяет, является ли идентификатор переносимым безопасным сегментом пути.</summary>
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

    /// <summary>Возвращает ожидаемое имя первичной DLL безопасного модуля.</summary>
    public static string GetPrimaryAssemblyFileName(string moduleId)
    {
        if (!IsSafeModuleId(moduleId))
        {
            throw new ArgumentException("Module ID must be a portable safe path segment.", nameof(moduleId));
        }

        return moduleId + ".dll";
    }
}
