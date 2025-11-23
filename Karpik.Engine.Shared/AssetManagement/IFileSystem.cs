namespace Karpik.Engine.Shared;

public interface IFileSystem
{
    public bool Exists(string path);
    public Stream OpenRead(string path);
    Stream OpenWrite(string path);
}