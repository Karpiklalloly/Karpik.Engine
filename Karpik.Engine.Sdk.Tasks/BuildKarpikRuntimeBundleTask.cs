using Microsoft.Build.Framework;

namespace Karpik.Engine.Sdk.Tasks;

public class RuntimeBundleFileSystem
{
    public virtual void MoveDirectory(string source, string destination) => Directory.Move(source, destination);
    public virtual void DeleteFile(string path) => File.Delete(path);
}

public sealed class BuildKarpikRuntimeBundleTask : Microsoft.Build.Utilities.Task
{
    public const string BundleCompletionMarker = "karpik-runtime-bundle-v1\n";
    public const string ModuleCompletionMarker = "karpik-module-staging-v1\n";
    public const string SideMarkerPrefix = "karpik-runtime-side-v1:";
    private const string OwnedStagingMarker = "karpik-runtime-owned-staging-v1\n";

    private readonly RuntimeBundleFileSystem _fileSystem;

    public BuildKarpikRuntimeBundleTask() : this(new RuntimeBundleFileSystem()) { }

    public BuildKarpikRuntimeBundleTask(RuntimeBundleFileSystem fileSystem) =>
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    [Required]
    public string Side { get; set; } = string.Empty;

    [Required]
    public string PrimaryAssembly { get; set; } = string.Empty;

    [Required]
    public string BundlePath { get; set; } = string.Empty;

    public ITaskItem[] Assemblies { get; set; } = [];

    public ITaskItem[] Content { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            Publish();
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.LogError($"KARPIK006: Runtime bundle publication failed: {exception.Message}");
            return false;
        }
    }

    private void Publish()
    {
        if (Side is not ("Client" or "Server"))
        {
            throw new ArgumentException("KarpikSide must be Client or Server for a runtime bundle.");
        }
        if (!Path.IsPathFullyQualified(BundlePath))
        {
            throw new ArgumentException("KarpikRuntimeBundlePath must be absolute.");
        }
        if (!Path.IsPathFullyQualified(PrimaryAssembly) || !File.Exists(PrimaryAssembly))
        {
            throw new FileNotFoundException("The primary game assembly must be an existing absolute path.", PrimaryAssembly);
        }
        if (IsReparsePoint(PrimaryAssembly))
        {
            throw new InvalidDataException($"The primary game assembly is a link or reparse point: {PrimaryAssembly}");
        }
        if (Content.Length == 0)
        {
            throw new InvalidDataException("A runtime bundle must contain at least one content item.");
        }

        string destination = TrimRoot(Path.GetFullPath(BundlePath));
        string? parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrEmpty(parent))
        {
            throw new ArgumentException("The runtime bundle destination must have a parent directory.");
        }
        Directory.CreateDirectory(parent);
        EnsureNotReparse(parent);

        string backup = destination + ".previous";
        RecoverBackup(destination, backup);
        RecoverOwnedStaging(parent, Path.GetFileName(destination));
        if (Directory.Exists(destination) && !IsCompleteBundle(destination, Side))
        {
            throw new InvalidDataException($"Refusing to replace unproven directory '{destination}'.");
        }

        string staging = destination + ".staging." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, ".karpik-owned-staging"), OwnedStagingMarker);
        try
        {
            Materialize(staging);
            if (!IsCompleteBundle(staging, Side))
            {
                throw new InvalidDataException("The staged runtime bundle did not pass completion validation.");
            }

            bool hadDestination = Directory.Exists(destination);
            if (hadDestination)
            {
                _fileSystem.MoveDirectory(destination, backup);
            }
            try
            {
                _fileSystem.MoveDirectory(staging, destination);
                _fileSystem.DeleteFile(Path.Combine(destination, ".karpik-owned-staging"));
            }
            catch
            {
                DeleteOwnedPublishedBundle(destination);
                if (!Directory.Exists(destination) && Directory.Exists(backup) && IsCompleteBundle(backup, Side))
                {
                    _fileSystem.MoveDirectory(backup, destination);
                }
                throw;
            }

            if (Directory.Exists(backup) && IsCompleteBundle(backup, Side))
            {
                Directory.Delete(backup, recursive: true);
            }
        }
        finally
        {
            DeleteOwnedStaging(staging);
        }
    }

    private void Materialize(string staging)
    {
        string modules = Path.Combine(staging, "modules.version.1");
        string contentRoot = Path.Combine(staging, "Content");
        Directory.CreateDirectory(modules);
        Directory.CreateDirectory(contentRoot);

        var sources = new SortedDictionary<string, string>(StringComparer.Ordinal);
        AddAssembly(sources, PrimaryAssembly);
        foreach (ITaskItem item in Assemblies)
        {
            AddAssembly(sources, item.ItemSpec);
        }
        foreach ((string fileName, string source) in sources)
        {
            File.Copy(source, Path.Combine(modules, fileName), overwrite: false);
        }
        File.WriteAllText(
            Path.Combine(modules, "modules.list"),
            string.Join('\n', sources.Keys) + '\n',
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.WriteAllText(Path.Combine(modules, ".complete"), ModuleCompletionMarker);

        var contentTargets = new HashSet<string>(PathComparer);
        foreach (ITaskItem item in Content.OrderBy(item => item.GetMetadata("TargetPath"), StringComparer.Ordinal))
        {
            string source = Path.GetFullPath(item.ItemSpec);
            if (!File.Exists(source) || IsReparsePoint(source))
            {
                throw new InvalidDataException($"Runtime content is missing or linked: {item.ItemSpec}");
            }
            string relative = item.GetMetadata("TargetPath");
            if (string.IsNullOrWhiteSpace(relative))
            {
                relative = Path.GetFileName(source);
            }
            relative = relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            string destination = Path.GetFullPath(Path.Combine(contentRoot, relative));
            if (!IsContained(contentRoot, destination) || !contentTargets.Add(destination))
            {
                throw new InvalidDataException($"Runtime content target escapes or conflicts within Content: {relative}");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
        }

        File.WriteAllText(Path.Combine(staging, "runtime-bundle.side"), SideMarkerPrefix + Side + "\n");
        File.WriteAllText(Path.Combine(staging, ".complete"), BundleCompletionMarker);
    }

    private static void AddAssembly(IDictionary<string, string> sources, string path)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path) || IsReparsePoint(path))
        {
            throw new InvalidDataException($"Runtime assembly is missing, relative, or linked: {path}");
        }
        string fileName = Path.GetFileName(path);
        if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Runtime assembly must be a .dll: {path}");
        }
        if (fileName.StartsWith("Karpik.Engine.Core.Runner", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The engine runner must never be copied into a game-owned bundle.");
        }
        string fullPath = Path.GetFullPath(path);
        if (sources.TryGetValue(fileName, out string? existing) && !PathComparer.Equals(existing, fullPath))
        {
            throw new InvalidDataException($"Two runtime assemblies have the same file name: {fileName}");
        }
        sources[fileName] = fullPath;
    }

    private void RecoverBackup(string destination, string backup)
    {
        if (!Directory.Exists(backup))
        {
            return;
        }
        if (IsReparsePoint(backup) || !IsCompleteBundle(backup, Side))
        {
            throw new InvalidDataException($"Refusing to move or delete unproven interrupted backup '{backup}'.");
        }
        if (!Directory.Exists(destination))
        {
            _fileSystem.MoveDirectory(backup, destination);
            return;
        }
        if (!IsCompleteBundle(destination, Side))
        {
            throw new InvalidDataException($"Interrupted bundle state contains an invalid destination '{destination}'.");
        }
        Directory.Delete(backup, recursive: true);
    }

    private static void RecoverOwnedStaging(string parent, string destinationName)
    {
        foreach (string candidate in Directory.EnumerateDirectories(parent, destinationName + ".staging.*", SearchOption.TopDirectoryOnly))
        {
            DeleteOwnedStaging(candidate);
        }
    }

    private static void DeleteOwnedStaging(string path)
    {
        if (!Directory.Exists(path) || IsReparsePoint(path))
        {
            return;
        }
        string marker = Path.Combine(path, ".karpik-owned-staging");
        if (File.Exists(marker) && File.ReadAllText(marker) == OwnedStagingMarker)
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void DeleteOwnedPublishedBundle(string path)
    {
        if (!Directory.Exists(path) || IsReparsePoint(path))
        {
            return;
        }
        string marker = Path.Combine(path, ".karpik-owned-staging");
        if (File.Exists(marker) && File.ReadAllText(marker) == OwnedStagingMarker)
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static bool IsCompleteBundle(string root, string side)
    {
        try
        {
            if (!Directory.Exists(root) || IsReparsePoint(root))
            {
                return false;
            }
            string modules = Path.Combine(root, "modules.version.1");
            return File.ReadAllText(Path.Combine(root, ".complete")) == BundleCompletionMarker
                   && File.ReadAllText(Path.Combine(root, "runtime-bundle.side")) == SideMarkerPrefix + side + "\n"
                   && Directory.Exists(Path.Combine(root, "Content"))
                   && Directory.EnumerateFiles(Path.Combine(root, "Content"), "*", SearchOption.AllDirectories).Any()
                   && Directory.Exists(modules)
                   && File.ReadAllText(Path.Combine(modules, ".complete")) == ModuleCompletionMarker
                   && File.ReadAllLines(Path.Combine(modules, "modules.list")).Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void EnsureNotReparse(string path)
    {
        var current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0 || current.LinkTarget is not null)
            {
                throw new InvalidDataException($"Runtime bundle path contains a link or reparse point: {current.FullName}");
            }
            current = current.Parent;
        }
    }

    private static bool IsContained(string root, string candidate)
    {
        string prefix = TrimRoot(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(candidate).StartsWith(prefix, PathComparison);
    }

    private static string TrimRoot(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsReparsePoint(string path)
    {
        var info = Directory.Exists(path) ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
        return (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
