namespace Karpik.Engine.Shared;

public class PhysicalFileSystem : IFileSystem
{
    public char DirectorySeparatorChar => Path.DirectorySeparatorChar;
    public bool Exists(string path) => File.Exists(path);
    public bool ExistsDirectory(string path) => Directory.Exists(path);
    public Stream OpenRead(string path) => File.OpenRead(path);
    public Stream OpenWrite(string path)
    {
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return new FileStream(path, FileMode.Create, FileAccess.Write);
    }

    public string Combine(params ReadOnlySpan<string> path) => Path.Combine(path);

    public Span<string> GetDirectories(string path) => Directory.GetDirectories(path);

    public Span<string> GetFiles(string path) => Directory.GetFiles(path);

    public Span<string> GetFiles(string path, string searchPattern, SearchOption searchOption) => Directory.GetFiles(path, searchPattern, searchOption);

    public string GetFileName(string path) => Path.GetFileName(path);
}