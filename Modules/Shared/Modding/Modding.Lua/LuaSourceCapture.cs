using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Karpik.Content.Core;

namespace Karpik.Engine.Shared.Modding.Lua;

internal static class LuaSourceCapture
{
    internal const int MaximumPackageFiles = 512;
    internal const int MaximumPackageBytes = 8 * 1024 * 1024;
    internal const int MaximumSetFiles = 4096;
    internal const int MaximumSetBytes = 64 * 1024 * 1024;
    internal const int MaximumPackages = 256;
    private const int MaximumTreeEntries = 2048;
    private const int MaximumRelativePath = 512;
    public static bool IsSupported => OperatingSystem.IsLinux() || OperatingSystem.IsWindows();
    internal static StringComparer PhysicalPathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static string SideDirectory(ExecutionSide side) => side switch
    {
        ExecutionSide.Client => "Client",
        ExecutionSide.Server => "Server",
        _ => throw new ArgumentOutOfRangeException(nameof(side))
    };

    internal static ModScriptSource Capture(string directory, ExecutionSide side, CancellationToken cancellation = default,
        ModScriptSource? required = null, Action<string>? verifyManifest = null,
        int remainingFiles = MaximumPackageFiles, int remainingBytes = MaximumPackageBytes)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("Verified Lua source capture requires Linux or Windows.");
        string sideName = SideDirectory(side);
        string root = ResolveRoot(directory);
        var first = Read(root, sideName, keepText: true, Math.Min(MaximumPackageFiles, remainingFiles),
            Math.Min(MaximumPackageBytes, remainingBytes), cancellation);
        // Metadata is observed between matching observations of the entire Lua projection.
        verifyManifest?.Invoke(root);
        if (!PhysicalPathComparer.Equals(root, ResolveRoot(directory))) throw Changed(directory);
        var second = Read(root, sideName, keepText: false, first.Files.Count, first.Bytes, cancellation);
        if (!PhysicalPathComparer.Equals(root, ResolveRoot(directory)) ||
            first.Files.Count != second.Files.Count || !first.Files.SequenceEqual(second.Files)) throw Changed(directory);
        verifyManifest?.Invoke(root);

        var nativeEntries = OperatingSystem.IsWindows() ? ObserveEntryPoints(root, side) : default;
        foreach (string entry in new[] { "Shared/main.lua", $"{sideName}/main.lua" })
        {
            bool captured = first.Text.ContainsKey(entry);
            if (!nativeEntries.IsDefault && captured != nativeEntries.Contains(entry))
                throw NativePathMismatch(Path.Combine(root, entry));
            if (!captured && required is not null && required.TryGetFile(entry, out _))
                throw new InvalidDataException($"Previously accepted Lua entry point '{Path.Combine(directory, entry)}' is missing.");
        }

        return new ModScriptSource(side, Identity(first.Files, "Shared/"), Identity(first.Files, sideName + "/"),
            first.Text.ToFrozenDictionary(first.Text.Comparer), first.Bytes);
    }

    internal static ImmutableArray<string> ObserveEntryPoints(string root, ExecutionSide side)
    {
        var observed = ImmutableArray.CreateBuilder<string>(2);
        foreach (string entry in new[] { "Shared/main.lua", $"{SideDirectory(side)}/main.lua" })
        {
            try { _ = File.GetAttributes(Path.Combine(root, entry)); }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { continue; }
            observed.Add(entry);
        }
        return observed.ToImmutable();
    }

    internal static bool HasEntryPoint(ModScriptSource source) => source.TryGetFile("Shared/main.lua", out _) ||
        source.TryGetFile($"{SideDirectory(source.Side)}/main.lua", out _);

    internal static string ResolveRoot(string directory)
    {
        string path = Path.GetFullPath(directory);
        string parent = Path.GetDirectoryName(path)!;
        if (new DirectoryInfo(parent).LinkTarget is not null)
            path = Path.Combine(Directory.ResolveLinkTarget(parent, returnFinalTarget: true)!.FullName, Path.GetFileName(path));
        if (new DirectoryInfo(path).LinkTarget is not null)
            path = Directory.ResolveLinkTarget(path, returnFinalTarget: true)!.FullName;
        if (!ContentCaptureFiles.DirectoryExists(path)) throw Changed(directory);
        return path;
    }

    private static Observation Read(string root, string side, bool keepText, int maximumFiles, int maximumBytes,
        CancellationToken cancellation)
    {
        var comparer = PhysicalPathComparer;
        // Keep observations and identities ordinal; only lookup folds Windows paths.
        var files = new SortedDictionary<string, ObservedFile>(StringComparer.Ordinal);
        var text = new Dictionary<string, string>(comparer);
        var foldedPaths = OperatingSystem.IsWindows() ? new HashSet<string>(comparer) : null;
        string sideEntry = side + "/main.lua";
        int bytes = 0, entries = 0;
        if (foldedPaths is not null)
        {
            // Preserve canonical slice prefixes but check for ambiguous on-disk aliases.
            foreach (string path in Directory.EnumerateFileSystemEntries(root))
            {
                cancellation.ThrowIfCancellationRequested();
                string name = Path.GetFileName(path);
                if (comparer.Equals(name, "Shared") || comparer.Equals(name, side)) RequireUniquePath(name);
            }
        }
        ReadDirectory("Shared");
        ReadDirectory(side);
        return new(files, text, bytes);

        void RequireUniquePath(string relative)
        {
            if (foldedPaths is null || foldedPaths.Add(relative)) return;
            foldedPaths.TryGetValue(relative, out var previous);
            throw new InvalidDataException($"Ambiguous Windows Lua source paths '{previous}' and '{relative}' in '{root}'.");
        }

        void ReadDirectory(string relativeDirectory)
        {
            cancellation.ThrowIfCancellationRequested();
            string directory = Path.Combine(root, relativeDirectory);
            if (!ContentCaptureFiles.DirectoryExists(directory))
            {
                if (foldedPaths?.Contains(relativeDirectory) == true) throw NativePathMismatch(directory);
                return;
            }
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellation.ThrowIfCancellationRequested();
                string name = Path.GetFileName(path);
                string relative = relativeDirectory + "/" + name;
                if (++entries > MaximumTreeEntries || relative.Length > MaximumRelativePath)
                    throw new InvalidDataException($"Lua source exceeds the path or tree limit at '{path}'.");
                if (name.Contains('\\') || name.Contains(':') || name.EndsWith('.') || name.EndsWith(' '))
                    throw new InvalidDataException($"Unsafe Lua source path '{path}'.");
                RequireUniquePath(relative);

                FileAttributes attributes = File.GetAttributes(path);
                bool linked = (attributes & FileAttributes.ReparsePoint) != 0;
                if ((attributes & FileAttributes.Directory) != 0 && !linked)
                {
                    if (comparer.Equals(relative, "Shared/main.lua") || comparer.Equals(relative, sideEntry))
                        throw new InvalidDataException($"Lua entry point must be a file: '{path}'.");
                    ReadDirectory(relative);
                    continue;
                }
                if (files.Count == maximumFiles)
                    throw new InvalidDataException($"Lua source exceeds the {maximumFiles} remaining file limit at '{path}'.");
                string target = linked ? ResolveFileLink(root, relative, path) : path;
                using var stream = ContentCaptureFiles.OpenRead(target) ?? throw Changed(path);
                long length = stream.Length;
                if (length > maximumBytes - bytes)
                    throw new InvalidDataException($"Lua source exceeds its byte limit at '{path}'.");
                DateTime written = File.GetLastWriteTimeUtc(target);
                var content = new byte[(int)length];
                stream.ReadExactly(content);
                if (stream.ReadByte() != -1 || File.GetLastWriteTimeUtc(target) != written) throw Changed(path);
                bytes += content.Length;
                files.Add(relative, new(target, written, Convert.ToHexString(SHA256.HashData(content))));
                if (!keepText) continue;
                using var reader = new StreamReader(new MemoryStream(content, writable: false));
                text.Add(relative, reader.ReadToEnd());
            }
        }
    }

    private static string ResolveFileLink(string root, string relative, string path)
    {
        string? target = File.ResolveLinkTarget(path, returnFinalTarget: false)?.FullName;
        string? targetRelative = target is null ? null : Path.GetRelativePath(root, target).Replace('\\', '/');
        if (targetRelative is null || Path.IsPathRooted(targetRelative) || targetRelative is "." or ".." ||
            targetRelative.StartsWith("../", StringComparison.Ordinal))
            throw new InvalidDataException($"Lua source link escapes the mod directory: '{path}'.");
        string sourceSlice = relative[..relative.IndexOf('/')];
        string targetSlice = targetRelative.Split('/', 2)[0];
        if (!PhysicalPathComparer.Equals(sourceSlice, targetSlice) &&
            (PhysicalPathComparer.Equals(targetSlice, "Shared") || PhysicalPathComparer.Equals(targetSlice, "Client") ||
             PhysicalPathComparer.Equals(targetSlice, "Server") || PhysicalPathComparer.Equals(targetSlice, "Content")))
            throw new InvalidDataException($"Lua source link crosses slices: '{path}'.");
        // OpenRead checks the target and all its ancestors without following further links.
        return target!;
    }

    private static string Identity(SortedDictionary<string, ObservedFile> files, string prefix)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(prefix));
        foreach (var (path, file) in files)
        {
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) continue;
            hash.AppendData(Encoding.UTF8.GetBytes(path));
            hash.AppendData("\0"u8); // NUL cannot occur in a file path; the hex digest has a fixed width.
            hash.AppendData(Encoding.ASCII.GetBytes(file.Hash));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static InvalidDataException Changed(string path) => new($"Lua source changed or disappeared during capture: '{path}'.");
    private static InvalidDataException NativePathMismatch(string path) => new($"Windows path lookup disagrees with the observed Lua source: '{path}'.");
    private sealed record Observation(SortedDictionary<string, ObservedFile> Files, Dictionary<string, string> Text, int Bytes);
    private readonly record struct ObservedFile(string Target, DateTime Written, string Hash);

    internal sealed class Budget
    {
        private int _files, _bytes, _packages;

        internal ModDefinition Pin(ModDefinition definition, ExecutionSide side, CancellationToken cancellation,
            bool fresh = false, Action<string>? verifyManifest = null)
        {
            if (++_packages > MaximumPackages)
                throw new InvalidDataException($"Lua mod set exceeds {MaximumPackages} packages.");
            if (definition.MetaData.ContentOnly) return definition;
            var previous = definition.ScriptSource;
            if (previous is not null && previous.Side != side)
                throw new InvalidDataException($"Lua source for '{definition.MetaData.Id}' was captured for {previous.Side}, not {side}.");
            var source = !fresh && previous is not null ? previous : Capture(definition.Directory, side, cancellation,
                previous, verifyManifest, MaximumSetFiles - _files, MaximumSetBytes - _bytes);
            _files += source.FileCount;
            _bytes += source.ByteCount;
            if (_files > MaximumSetFiles || _bytes > MaximumSetBytes)
                throw new InvalidDataException($"Lua mod set exceeds the aggregate file or byte limit at '{definition.Directory}'.");
            return definition with { ScriptSource = source };
        }
    }
}
