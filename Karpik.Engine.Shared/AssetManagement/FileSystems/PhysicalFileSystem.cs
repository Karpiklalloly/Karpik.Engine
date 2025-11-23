namespace Karpik.Engine.Shared;

public class PhysicalFileSystem : IFileSystem
{
    public bool Exists(string path) => File.Exists(path);
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
}