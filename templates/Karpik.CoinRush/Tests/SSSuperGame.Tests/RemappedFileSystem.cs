using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Karpik.Engine.Core.FileSystem;
using Xunit;

namespace SSSuperGame.Tests;

/// <summary>Maps engine file-system roots to test directories.</summary>
public sealed class RemappedFileSystem : IFileSystem
{
    private readonly PhysicalFileSystem _inner = new();
    private readonly string _root;
    private readonly string _content;

    /// <summary>Creates a file system with explicit root and content directories.</summary>
    /// <param name="root">The file-system root directory.</param>
    /// <param name="content">The content directory.</param>
    public RemappedFileSystem(string root, string content)
    {
        _root = root;
        _content = content;
    }

    /// <summary>The remapped root directory.</summary>
    public string RootPath => _root;
    /// <summary>The remapped content directory.</summary>
    public string ContentPath => _content;
    /// <summary>The underlying file system mods directory.</summary>
    public string ModsPath => _inner.ModsPath;
    /// <summary>The platform directory separator.</summary>
    public char DirectorySeparatorChar => _inner.DirectorySeparatorChar;
    /// <summary>Checks whether a file exists at the supplied path.</summary>
    /// <param name="path">The file or directory path.</param>
    /// <returns>Whether the file exists.</returns>
    public bool Exists(string path) => _inner.Exists(path);
    /// <summary>Gets the extension of a path.</summary>
    /// <param name="path">The file or directory path.</param>
    /// <returns>The file extension.</returns>
    public string GetExtension(string path) => _inner.GetExtension(path);
    /// <summary>Checks whether a directory exists at the supplied path.</summary>
    /// <param name="path">The file or directory path.</param>
    /// <returns>Whether the directory exists.</returns>
    public bool ExistsDirectory(string path) => _inner.ExistsDirectory(path);
    /// <summary>Opens a file for reading.</summary>
    /// <param name="path">The file or directory path.</param>
    /// <returns>A readable stream.</returns>
    public Stream OpenRead(string path) => _inner.OpenRead(path);
    /// <summary>Opens a file for writing.</summary>
    /// <param name="path">The file or directory path.</param>
    /// <returns>A writable stream.</returns>
    public Stream OpenWrite(string path) => _inner.OpenWrite(path);
    /// <summary>Combines file-system path segments.</summary>
    /// <param name="path">The file or directory path.</param>
    /// <returns>The combined path.</returns>
    public string Combine(params ReadOnlySpan<string> path) => _inner.Combine(path);
    /// <summary>Lists child directories at the supplied path.</summary>
    /// <param name="path">The file or directory path.</param>
    /// <returns>The child directory paths.</returns>
    public Span<string> GetDirectories(string path) => _inner.GetDirectories(path);
    /// <summary>Lists files at the supplied path.</summary>
    /// <param name="path">The file or directory path.</param>
    /// <returns>The matching file paths.</returns>
    public Span<string> GetFiles(string path) => _inner.GetFiles(path);
    /// <summary>Lists files at the supplied path.</summary>
    /// <param name="path">The file or directory path.</param>
    /// <param name="searchPattern">The file-name search pattern.</param>
    /// <param name="searchOption">The directory traversal option.</param>
    /// <returns>The matching file paths.</returns>
    public Span<string> GetFiles(string path, string searchPattern, SearchOption searchOption) => _inner.GetFiles(path, searchPattern, searchOption);
    /// <summary>Gets the file name from a path.</summary>
    /// <param name="path">The file or directory path.</param>
    /// <returns>The final path segment.</returns>
    public string GetFileName(string path) => _inner.GetFileName(path);
}
