namespace Karpik.Engine.ProjectModel;

/// <summary>Описывает результат попытки прочитать project-файл.</summary>
internal enum KarpikProjectReadStatus
{
    /// <summary>Файл успешно прочитан.</summary>
    Success,
    /// <summary>Файл отсутствует.</summary>
    Missing,
    /// <summary>Файл существует, но недоступен для чтения.</summary>
    Unreadable,
    /// <summary>Файл находится за пределами корня решения.</summary>
    OutsideSolutionRoot
}
