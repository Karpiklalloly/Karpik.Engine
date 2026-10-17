namespace Karpik.Engine.ProjectModel;

/// <summary>
/// Содержит стабильные коды диагностики, выдаваемые SDK при проверке проекта.
/// </summary>
public static class KarpikDiagnosticCodes
{
    /// <summary>Проект не использует обязательный Karpik SDK.</summary>
    public const string MissingSdk = "KARPIK001";
    /// <summary>Указан неподдерживаемый вид проекта.</summary>
    public const string InvalidProjectKind = "KARPIK002";
    /// <summary>Указана неподдерживаемая сторона проекта.</summary>
    public const string InvalidProjectSide = "KARPIK003";
    /// <summary>Структура решения или ссылка на проект нарушает контракт SDK.</summary>
    public const string InvalidSolutionProject = "KARPIK004";
    /// <summary>Ссылка пересекает запрещённую границу Client/Server/Shared.</summary>
    public const string ForbiddenSideDependency = "KARPIK005";
    /// <summary>В графе ссылок на проекты найден цикл.</summary>
    public const string ProjectReferenceCycle = "KARPIK006";
    /// <summary>Модуль неизвестен или его реализация неоднозначна.</summary>
    public const string UnknownOrAmbiguousModule = "KARPIK007";
    /// <summary>Не найден обязательный модуль runtime-проекта.</summary>
    public const string MissingRequiredModule = "KARPIK008";
    /// <summary>Не удалось разрешить совместимую установку движка.</summary>
    public const string EngineInstallationResolutionFailed = "KARPIK009";
}
