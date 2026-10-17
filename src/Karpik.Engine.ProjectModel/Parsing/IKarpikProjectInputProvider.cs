namespace Karpik.Engine.ProjectModel;

/// <summary>Предоставляет файловый ввод для чтения solution и project-файлов.</summary>
public interface IKarpikProjectInputProvider
{
    /// <summary>Проверяет существование файла по абсолютному пути.</summary>
    bool Exists(string absolutePath);

    /// <summary>Открывает существующий файл для чтения.</summary>
    Stream OpenRead(string absolutePath);
}

/// <summary>Реализация input provider поверх локальной файловой системы.</summary>
internal sealed class FileSystemKarpikProjectInputProvider : IKarpikProjectInputProvider
{
    /// <summary>Получает общий экземпляр провайдера файловой системы.</summary>
    public static FileSystemKarpikProjectInputProvider Instance { get; } = new();

    /// <summary>Инициализирует единственный экземпляр провайдера.</summary>
    private FileSystemKarpikProjectInputProvider()
    {
    }

    /// <inheritdoc />
    public bool Exists(string absolutePath) => File.Exists(absolutePath);

    /// <inheritdoc />
    public Stream OpenRead(string absolutePath) => File.OpenRead(absolutePath);
}
