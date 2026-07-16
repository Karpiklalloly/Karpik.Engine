namespace Karpik.Engine.ProjectModel;

public interface IKarpikProjectInputProvider
{
    bool Exists(string absolutePath);

    Stream OpenRead(string absolutePath);
}

internal sealed class FileSystemKarpikProjectInputProvider : IKarpikProjectInputProvider
{
    public static FileSystemKarpikProjectInputProvider Instance { get; } = new();

    private FileSystemKarpikProjectInputProvider()
    {
    }

    public bool Exists(string absolutePath) => File.Exists(absolutePath);

    public Stream OpenRead(string absolutePath) => File.OpenRead(absolutePath);
}
