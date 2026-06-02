namespace Karpik.Engine.Shared.AssetManagement.Core;

public interface IFileSystem
{
    public char DirectorySeparatorChar { get; }
    public bool Exists(string path);
    public string GetExtension(string path);
    public bool ExistsDirectory(string path);
    public Stream OpenRead(string path);
    public Stream OpenWrite(string path);
    public string Combine(params ReadOnlySpan<string> path);
    public Span<string> GetDirectories(string path);
    public Span<string> GetFiles(string path);
    public Span<string> GetFiles(string path, string searchPattern, SearchOption searchOption);
    public string GetFileName(string path);
}