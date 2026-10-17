namespace Karpik.Engine.ProjectModel;

/// <summary>Определяет роль проекта в Karpik-сборке.</summary>
public enum KarpikProjectKind
{
    /// <summary>Исполняемый client-, server- или shared-runtime проект.</summary>
    Runtime,
    /// <summary>Тестовый проект.</summary>
    Test,
    /// <summary>Инструментальный проект, выполняемый во время разработки или сборки.</summary>
    Tool,
    /// <summary>Проект source generator или analyzer.</summary>
    Generator,
    /// <summary>Проект authoring- или build-ресурсов.</summary>
    Assets
}
