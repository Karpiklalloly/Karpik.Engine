using System.Composition;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.AssetManagement.Core.Physical;

[Export(typeof(IFileSystem))]
[Export(typeof(PhysicalFileSystem))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public class PhysicalFileSystem : IFileSystem
{
    public string RootPath { get; } = Path.GetFullPath(Environment.CurrentDirectory);
    public string ContentPath => Combine(RootPath, "Content");
    public string ModsPath => Combine(RootPath, "Mods");
    public char DirectorySeparatorChar => Path.DirectorySeparatorChar;
    
    public bool Exists(string path) => File.Exists(path);
    public string GetExtension(string path) => Path.GetExtension(path);

    public bool ExistsDirectory(string path) => Directory.Exists(path);
    public Stream OpenRead(string path) => File.OpenRead(path);
    public Stream OpenWrite(string path)
    {
        string? dir = Path.GetDirectoryName(path);
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