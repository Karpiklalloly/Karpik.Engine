using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Karpik.Engine.Tooling;

public enum EngineInstallationValidationCode
{
    Valid,
    InvalidPath,
    ReparsePoint,
    MissingManifest,
    CorruptManifest,
    ManifestContractMismatch,
    InvalidManifest,
    WrongEngineVersion,
    WrongSdkVersion,
    WrongEditorVersion,
    WrongLayoutVersion,
    WrongRuntimeProtocolVersion,
    MissingDirectory,
    MissingRunner,
    MissingEditor,
    MissingSdkPackage,
    MissingModules,
    MissingCompletionMarker,
    HashMismatch
}

public sealed record EngineInstallationValidationResult(
    bool IsValid,
    EngineInstallationValidationCode Code,
    string Message,
    EngineInstallationManifest? Manifest = null);

public sealed class EngineInstallationValidator
{
    public EngineInstallationValidationResult Validate(
        string installationRoot,
        string? expectedSdkVersion = null,
        string? expectedEngineVersion = null)
    {
        string root;
        try
        {
            root = Path.GetFullPath(installationRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(EngineInstallationValidationCode.InvalidPath, $"Invalid engine installation path: {exception.Message}");
        }

        if (!Directory.Exists(root))
        {
            return Failure(EngineInstallationValidationCode.InvalidPath, $"Engine installation directory does not exist: {root}");
        }
        if (PathSafety.IsReparsePoint(root))
        {
            return Failure(EngineInstallationValidationCode.ReparsePoint, $"Engine installation root is a link or reparse point: {root}");
        }

        string manifestPath = Path.Combine(root, "engine-installation.json");
        if (!File.Exists(manifestPath))
        {
            return Failure(EngineInstallationValidationCode.MissingManifest, $"Missing engine installation manifest: {manifestPath}");
        }

        EngineInstallationManifest manifest;
        try
        {
            manifest = EngineInstallationManifest.Parse(File.ReadAllText(manifestPath));
        }
        catch (ManifestContractException exception)
        {
            return Failure(EngineInstallationValidationCode.ManifestContractMismatch, exception.Message);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return Failure(EngineInstallationValidationCode.CorruptManifest, $"Cannot read engine installation manifest: {exception.Message}");
        }

        if (!IsValidVersion(manifest.EngineVersion) ||
            !IsValidVersion(manifest.MsBuildSdkVersion) ||
            !IsValidVersion(manifest.EditorVersion) ||
            manifest.ContentHash.Length != 64 ||
            manifest.ContentHash.Any(character => !Uri.IsHexDigit(character)))
        {
            return Failure(EngineInstallationValidationCode.InvalidManifest, "The engine installation manifest contains an invalid version or content hash.", manifest);
        }
        if (expectedEngineVersion is not null && !string.Equals(manifest.EngineVersion, expectedEngineVersion, StringComparison.Ordinal))
        {
            return Failure(EngineInstallationValidationCode.WrongEngineVersion, $"Expected engine version '{expectedEngineVersion}', found '{manifest.EngineVersion}'.", manifest);
        }
        if (expectedSdkVersion is not null && !string.Equals(manifest.MsBuildSdkVersion, expectedSdkVersion, StringComparison.Ordinal))
        {
            return Failure(EngineInstallationValidationCode.WrongSdkVersion, $"Expected MSBuild SDK version '{expectedSdkVersion}', found '{manifest.MsBuildSdkVersion}'.", manifest);
        }
        if (!string.Equals(manifest.EditorVersion, manifest.EngineVersion, StringComparison.Ordinal))
        {
            return Failure(EngineInstallationValidationCode.WrongEditorVersion, $"Editor version '{manifest.EditorVersion}' does not match engine version '{manifest.EngineVersion}'.", manifest);
        }
        if (manifest.LayoutVersion != EngineInstallationManifest.CurrentLayoutVersion)
        {
            return Failure(EngineInstallationValidationCode.WrongLayoutVersion, $"Unsupported payload layout version '{manifest.LayoutVersion}'.", manifest);
        }
        if (manifest.RuntimeProtocolVersion != EngineInstallationManifest.CurrentRuntimeProtocolVersion)
        {
            return Failure(EngineInstallationValidationCode.WrongRuntimeProtocolVersion, $"Unsupported runtime protocol version '{manifest.RuntimeProtocolVersion}'.", manifest);
        }

        string completionMarker = Path.Combine(root, ".complete");
        if (!File.Exists(completionMarker))
        {
            return Failure(EngineInstallationValidationCode.MissingCompletionMarker, $"Missing completion marker: {completionMarker}", manifest);
        }

        string[] requiredDirectories = ["editor", "sdk", "runners/client", "runners/server", "modules", "native"];
        foreach (string relativeDirectory in requiredDirectories)
        {
            string directory = Path.Combine(root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory))
            {
                return Failure(EngineInstallationValidationCode.MissingDirectory, $"Missing payload directory '{relativeDirectory}'.", manifest);
            }
            if (PathSafety.IsReparsePoint(directory))
            {
                return Failure(EngineInstallationValidationCode.ReparsePoint, $"Payload directory '{relativeDirectory}' is a link or reparse point.", manifest);
            }
        }

        foreach (string side in new[] { "client", "server" })
        {
            string runner = Path.Combine(root, "runners", side, "Karpik.Engine.Core.Runner.dll");
            if (!File.Exists(runner))
            {
                return Failure(EngineInstallationValidationCode.MissingRunner, $"Missing {side} runner assembly: {runner}", manifest);
            }
        }
        if (!Directory.EnumerateFiles(Path.Combine(root, "editor"), "*", SearchOption.TopDirectoryOnly).Any())
        {
            return Failure(EngineInstallationValidationCode.MissingEditor, "The editor payload directory is empty.", manifest);
        }
        if (!Directory.EnumerateFiles(Path.Combine(root, "sdk"), "*.nupkg", SearchOption.TopDirectoryOnly).Any())
        {
            return Failure(EngineInstallationValidationCode.MissingSdkPackage, "The SDK payload directory contains no .nupkg file.", manifest);
        }
        string modulesRoot = Path.Combine(root, "modules");
        string[] moduleEntries = Directory.EnumerateFileSystemEntries(modulesRoot, "*", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (moduleEntries.Length == 0)
        {
            return InvalidModuleLayout(manifest);
        }

        var moduleIds = new HashSet<string>(ModuleLayoutPolicy.ModuleIdComparer);
        foreach (string entry in moduleEntries)
        {
            if (!PathSafety.IsContained(modulesRoot, entry))
            {
                return Failure(EngineInstallationValidationCode.InvalidPath, $"Module payload entry escapes the modules root: {entry}", manifest);
            }
            if (PathSafety.IsReparsePoint(entry))
            {
                return Failure(EngineInstallationValidationCode.ReparsePoint, $"Module payload entry is a link or reparse point: {entry}", manifest);
            }
            if (!Directory.Exists(entry))
            {
                return InvalidModuleLayout(manifest, $"Unexpected entry directly below modules/: {entry}");
            }

            string moduleId = Path.GetFileName(entry);
            if (!ModuleLayoutPolicy.IsSafeModuleId(moduleId) || !moduleIds.Add(moduleId))
            {
                return InvalidModuleLayout(manifest, $"Module ID is unsafe or not unique: {moduleId}");
            }

            string primaryAssembly = Path.Combine(entry, ModuleLayoutPolicy.GetPrimaryAssemblyFileName(moduleId));
            if (!PathSafety.IsContained(entry, primaryAssembly) || !File.Exists(primaryAssembly))
            {
                return InvalidModuleLayout(manifest, $"Missing primary module assembly: {primaryAssembly}");
            }
            if (PathSafety.IsReparsePoint(primaryAssembly))
            {
                return Failure(EngineInstallationValidationCode.ReparsePoint, $"Primary module assembly is a link or reparse point: {primaryAssembly}", manifest);
            }
        }

        string contentHash;
        try
        {
            contentHash = EngineContentHash.Compute(root);
        }
        catch (InvalidDataException exception)
        {
            return Failure(EngineInstallationValidationCode.ReparsePoint, exception.Message, manifest);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Failure(EngineInstallationValidationCode.InvalidPath, $"Cannot hash engine installation: {exception.Message}", manifest);
        }

        if (!string.Equals(contentHash, manifest.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            return Failure(EngineInstallationValidationCode.HashMismatch, $"Payload content hash mismatch. Expected '{manifest.ContentHash}', computed '{contentHash}'.", manifest);
        }

        return new EngineInstallationValidationResult(true, EngineInstallationValidationCode.Valid, "Engine installation is valid.", manifest);
    }

    private static bool IsValidVersion(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value is not "." and not ".." &&
        value.IndexOfAny(['/', '\\']) < 0 &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static EngineInstallationValidationResult Failure(
        EngineInstallationValidationCode code,
        string message,
        EngineInstallationManifest? manifest = null) => new(false, code, message, manifest);

    private static EngineInstallationValidationResult InvalidModuleLayout(
        EngineInstallationManifest manifest,
        string? detail = null) => Failure(
            EngineInstallationValidationCode.MissingModules,
            "Each module payload must be a safe unique directory below modules/<module-id>/ with its primary assembly at modules/<module-id>/<module-id>.dll." +
            (detail is null ? string.Empty : " " + detail),
            manifest);
}

public static class EngineContentHash
{
    private static readonly byte[] FormatPrefix = Encoding.ASCII.GetBytes("KARPIK-PAYLOAD-HASH\0v1");
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Compute(string installationRoot)
    {
        string root = Path.GetFullPath(installationRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(root);
        }
        if (PathSafety.IsReparsePoint(root))
        {
            throw new InvalidDataException($"Hash root is a link or reparse point: {root}");
        }

        List<(string RelativePath, string FullPath)> files = CollectFiles(root);
        files.Sort((left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        for (int index = 1; index < files.Count; index++)
        {
            if (string.Equals(files[index - 1].RelativePath, files[index].RelativePath, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Two files normalize to the same payload path: {files[index].RelativePath}");
            }
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(FormatPrefix);
        Span<byte> frame = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(frame, checked((ulong)files.Count));
        hash.AppendData(frame);
        byte[] buffer = new byte[128 * 1024];
        foreach ((string relativePath, string fullPath) in files)
        {
            byte[] pathBytes = StrictUtf8.GetBytes(relativePath);
            BinaryPrimitives.WriteUInt64BigEndian(frame, checked((ulong)pathBytes.Length));
            hash.AppendData(frame);
            hash.AppendData(pathBytes);

            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, FileOptions.SequentialScan);
            long length = stream.Length;
            BinaryPrimitives.WriteUInt64BigEndian(frame, checked((ulong)length));
            hash.AppendData(frame);
            long consumed = 0;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
            {
                hash.AppendData(buffer.AsSpan(0, read));
                consumed += read;
            }
            if (consumed != length)
            {
                throw new IOException($"Payload file changed while hashing: {fullPath}");
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static List<(string RelativePath, string FullPath)> CollectFiles(string root)
    {
        var files = new List<(string RelativePath, string FullPath)>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            if (PathSafety.IsReparsePoint(directory))
            {
                throw new InvalidDataException($"Payload contains a linked or reparse directory: {directory}");
            }

            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (!PathSafety.IsContained(root, entry))
                {
                    throw new InvalidDataException($"Payload entry escapes its root: {entry}");
                }
                if (PathSafety.IsReparsePoint(entry))
                {
                    throw new InvalidDataException($"Payload contains a link or reparse point: {entry}");
                }
                if (Directory.Exists(entry))
                {
                    pending.Push(entry);
                    continue;
                }
                if (!File.Exists(entry))
                {
                    throw new InvalidDataException($"Unsupported payload filesystem entry: {entry}");
                }

                string relativePath = NormalizeRelativePath(Path.GetRelativePath(root, entry));
                if (relativePath is "engine-installation.json" or ".complete")
                {
                    continue;
                }
                files.Add((relativePath, entry));
            }
        }
        return files;
    }

    private static string NormalizeRelativePath(string path)
    {
        string normalized = string.Join('/', path.Split(Path.DirectorySeparatorChar)).Normalize(NormalizationForm.FormC);
        if (normalized.Length == 0 || normalized == "." || normalized.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(normalized))
        {
            throw new InvalidDataException($"Invalid payload relative path: {path}");
        }
        return normalized;
    }
}

internal static class PathSafety
{
    public static bool IsContained(string root, string candidate)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullCandidate = Path.GetFullPath(candidate);
        return string.Equals(fullRoot, fullCandidate, PathComparison) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    public static bool IsReparsePoint(string path)
    {
        var info = Directory.Exists(path) ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
        return (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;
    }

    public static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
