using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Karpik.Engine.Tooling;

/// <summary>Описывает результат проверки структуры и совместимости engine payload.</summary>
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
    ConflictingManagedAssemblyIdentity,
    MissingCompletionMarker,
    HashMismatch
}

/// <summary>Содержит результат проверки одной установки движка.</summary>
/// <param name="IsValid">Указывает, прошла ли установка все проверки.</param>
/// <param name="Code">Код результата проверки.</param>
/// <param name="Message">Текст результата для диагностики.</param>
/// <param name="Manifest">Распарсенный manifest, если он доступен.</param>
public sealed record EngineInstallationValidationResult(
    bool IsValid,
    EngineInstallationValidationCode Code,
    string Message,
    EngineInstallationManifest? Manifest = null);

/// <summary>Проверяет manifest, layout, DLL-идентичности и content hash engine payload.</summary>
public sealed class EngineInstallationValidator
{
    /// <summary>Проверяет установку и, при необходимости, её ожидаемые версии.</summary>
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
        if (manifest.LayoutVersion < EngineInstallationManifest.MinimumLayoutVersion ||
            manifest.LayoutVersion > EngineInstallationManifest.CurrentLayoutVersion)
        {
            return Failure(EngineInstallationValidationCode.WrongLayoutVersion, $"Unsupported payload layout version '{manifest.LayoutVersion}'.", manifest);
        }
        bool sharedLayout = manifest.LayoutVersion >= 3;
        if (manifest.RuntimeProtocolVersion != EngineInstallationManifest.CurrentRuntimeProtocolVersion)
        {
            return Failure(EngineInstallationValidationCode.WrongRuntimeProtocolVersion, $"Unsupported runtime protocol version '{manifest.RuntimeProtocolVersion}'.", manifest);
        }

        string completionMarker = Path.Combine(root, ".complete");
        if (!File.Exists(completionMarker))
        {
            return Failure(EngineInstallationValidationCode.MissingCompletionMarker, $"Missing completion marker: {completionMarker}", manifest);
        }

        string[] requiredDirectories = sharedLayout
            ? ["editor", "sdk", "runners/client", "runners/server", "modules", "native", "shared"]
            : ["editor", "sdk", "runners/client", "runners/server", "modules", "native"];
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
        string editorAssembly = Path.Combine(root, "editor", "Karpik.Editor.dll");
        if (!File.Exists(editorAssembly))
        {
            return Failure(
                EngineInstallationValidationCode.MissingEditor,
                $"The editor payload does not contain its managed entry point: {editorAssembly}",
                manifest);
        }
        if (PathSafety.IsReparsePoint(editorAssembly))
        {
            return Failure(
                EngineInstallationValidationCode.ReparsePoint,
                $"The editor entry point is a link or reparse point: {editorAssembly}",
                manifest);
        }
        if (!Directory.EnumerateFiles(Path.Combine(root, "sdk"), "*.nupkg", SearchOption.TopDirectoryOnly).Any())
        {
            return Failure(EngineInstallationValidationCode.MissingSdkPackage, "The SDK payload directory contains no .nupkg file.", manifest);
        }
        string modulesRoot = Path.Combine(root, "modules");
        EngineModuleCatalogEntry[] catalog;
        try
        {
            catalog = EngineModuleCatalog.Read(modulesRoot);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return InvalidModuleLayout(manifest, exception.Message);
        }
        string[] allModuleEntries = Directory.EnumerateFileSystemEntries(modulesRoot, "*", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (string entry in allModuleEntries)
        {
            if (File.Exists(entry) && string.Equals(Path.GetFileName(entry), EngineModuleCatalog.FileName, StringComparison.Ordinal))
            {
                continue;
            }
            if (!Directory.Exists(entry))
            {
                return InvalidModuleLayout(manifest, $"Unexpected entry directly below modules/: {entry}");
            }
        }
        string[] moduleEntries = allModuleEntries.Where(Directory.Exists).ToArray();
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
            if (sharedLayout)
            {
                string[] moduleContents = Directory.EnumerateFileSystemEntries(entry).ToArray();
                if (moduleContents.Length != 1 ||
                    !string.Equals(Path.GetFullPath(moduleContents[0]), Path.GetFullPath(primaryAssembly), PathSafety.PathComparison))
                {
                    return InvalidModuleLayout(manifest, $"Layout v3 keeps only the primary assembly in a module directory: {entry}");
                }
            }
            if (PathSafety.IsReparsePoint(primaryAssembly))
            {
                return Failure(EngineInstallationValidationCode.ReparsePoint, $"Primary module assembly is a link or reparse point: {primaryAssembly}", manifest);
            }
        }
        if (!moduleIds.SetEquals(catalog.Select(entry => entry.ModuleId)))
        {
            return InvalidModuleLayout(manifest, "The module catalog and modules/<module-id>/ directories do not match exactly.");
        }
        IEnumerable<string> identityRoots = sharedLayout
            ? moduleEntries.Append(Path.Combine(root, "shared"))
            : moduleEntries;
        EngineInstallationValidationResult? identityConflict = ValidateManagedAssemblyIdentities(identityRoots, manifest);
        if (identityConflict is not null)
        {
            return identityConflict;
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

    /// <summary>Проверяет, пригодна ли версия для хранения в manifest и сегменте пути.</summary>
    private static bool IsValidVersion(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value is not "." and not ".." &&
        value.IndexOfAny(['/', '\\']) < 0 &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>Отклоняет module payload с конфликтующими CLR-идентичностями DLL.</summary>
    private static EngineInstallationValidationResult? ValidateManagedAssemblyIdentities(
        IEnumerable<string> moduleDirectories,
        EngineInstallationManifest manifest)
    {
        var identities = new Dictionary<string, (string FullName, string Hash, string Path)>(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in moduleDirectories.Order(StringComparer.Ordinal))
        {
            foreach (string path in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
            {
                System.Reflection.AssemblyName identity;
                try
                {
                    identity = System.Reflection.AssemblyName.GetAssemblyName(path);
                }
                catch (BadImageFormatException)
                {
                    continue;
                }
                string fullName = identity.FullName ?? identity.Name ?? Path.GetFileNameWithoutExtension(path);
                using FileStream stream = File.OpenRead(path);
                string hash = Convert.ToHexString(SHA256.HashData(stream));
                string simpleName = identity.Name ?? Path.GetFileNameWithoutExtension(path);
                if (identities.TryGetValue(simpleName, out (string FullName, string Hash, string Path) existing))
                {
                    if (!string.Equals(existing.FullName, fullName, StringComparison.Ordinal))
                    {
                        return Failure(
                            EngineInstallationValidationCode.ConflictingManagedAssemblyIdentity,
                            $"Module payload contains assemblies with the same simple name '{simpleName}' but different identities: '{existing.FullName}' at '{existing.Path}' and '{fullName}' at '{path}'.",
                            manifest);
                    }
                    if (!string.Equals(existing.Hash, hash, StringComparison.Ordinal))
                    {
                        return Failure(
                            EngineInstallationValidationCode.ConflictingManagedAssemblyIdentity,
                            $"Module payload contains byte-distinct assemblies with the same identity '{fullName}': '{existing.Path}' and '{path}'.",
                            manifest);
                    }
                    continue;
                }
                identities.Add(simpleName, (fullName, hash, path));
            }
        }
        return null;
    }

    /// <summary>Создаёт неуспешный результат валидации.</summary>
    private static EngineInstallationValidationResult Failure(
        EngineInstallationValidationCode code,
        string message,
        EngineInstallationManifest? manifest = null) => new(false, code, message, manifest);

    /// <summary>Создаёт диагностику некорректного layout каталога modules.</summary>
    private static EngineInstallationValidationResult InvalidModuleLayout(
        EngineInstallationManifest manifest,
        string? detail = null) => Failure(
            EngineInstallationValidationCode.MissingModules,
            "Each module payload must be a safe unique directory below modules/<module-id>/ with its primary assembly at modules/<module-id>/<module-id>.dll." +
            (detail is null ? string.Empty : " " + detail),
            manifest);
}

/// <summary>Вычисляет детерминированный SHA-256 хеш файлов engine payload.</summary>
public static class EngineContentHash
{
    /// <summary>Префикс формата, разделяющий версии алгоритма хеширования.</summary>
    private static readonly byte[] FormatPrefix = Encoding.ASCII.GetBytes("KARPIK-PAYLOAD-HASH\0v1");
    /// <summary>Строгий UTF-8 encoder для имён файлов payload.</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Вычисляет content hash, исключая installation manifest и completion marker.</summary>
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

    /// <summary>Собирает безопасные файлы payload и их нормализованные относительные пути.</summary>
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

    /// <summary>Нормализует относительный путь для стабильного хеширования.</summary>
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

/// <summary>Содержит проверки containment и reparse point для путей payload.</summary>
internal static class PathSafety
{
    /// <summary>Проверяет, расположен ли кандидат внутри корня.</summary>
    public static bool IsContained(string root, string candidate)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullCandidate = Path.GetFullPath(candidate);
        return string.Equals(fullRoot, fullCandidate, PathComparison) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    /// <summary>Определяет, является ли путь symbolic link или reparse point.</summary>
    public static bool IsReparsePoint(string path)
    {
        var info = Directory.Exists(path) ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
        return (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;
    }

    /// <summary>Получает платформенно-зависимое правило сравнения путей.</summary>
    public static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
