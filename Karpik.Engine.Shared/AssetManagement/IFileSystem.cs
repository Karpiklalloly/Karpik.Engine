namespace Karpik.Engine.Shared;

public interface IFileSystem
{
    public bool Exists(string path);
    public Stream OpenRead(string path);
    public Stream OpenWrite(string path);
    public string Combine(params ReadOnlySpan<string> path);
}