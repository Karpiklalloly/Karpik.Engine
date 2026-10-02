namespace Karpik.Engine.ProjectModel;

/// <summary>Определяет сторону dependency boundary проекта.</summary>
public enum KarpikProjectSide
{
    /// <summary>Клиентская сторона.</summary>
    Client,
    /// <summary>Серверная сторона.</summary>
    Server,
    /// <summary>Общая для клиента и сервера сторона.</summary>
    Shared,
    /// <summary>Сторона не требуется для данного вида проекта.</summary>
    None
}
