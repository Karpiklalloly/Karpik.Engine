using System.Composition;
using Karpik.Content.Core;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Karpik.Engine.Shared.Modding.Lua;

[Export(typeof(IModsRegistry))]
[ServiceRegistration(ModuleScope.ModSet, ServiceLifetime.Singleton)]
public class ModsRegistry : IModsRegistry
{
    private static readonly string[] EntryPointDirectories = ["Shared", "Client", "Server"];
    private readonly string _modsDirectory;
    private readonly ILogger<IModsRegistry> _logger;
    private ModDiscoveryResult? _startupDiscovery;
    private int _scanStarted;

    // assetsManager is retained for source compatibility; discovery never uses it.
    public ModsRegistry(IAssetsManager? assetsManager = null, ILogger<IModsRegistry>? logger = null, IFileSystem? fileSystem = null)
    {
        _logger = logger ?? NullLogger<IModsRegistry>.Instance;
        _modsDirectory = Path.GetFullPath(Path.Combine(fileSystem?.RootPath ?? Environment.CurrentDirectory, IModsRegistry.ModsDirectory));
    }

    public JobHandle Scan(ExecutionSide side, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref _scanStarted, 1) != 0)
                throw new InvalidOperationException("ModSet catalog is already scanning or prepared.");

            try
            {
                AssetTarget target = side switch
                {
                    ExecutionSide.Client => AssetTarget.Client,
                    ExecutionSide.Server => AssetTarget.Server,
                    _ => throw new ArgumentOutOfRangeException(nameof(side))
                };
                ModDiscoveryResult startup = Discover(target, cancellationToken);
                foreach (string diagnostic in startup.Diagnostics)
                    _logger.LogError("{Diagnostic}", diagnostic);
                foreach (ModDefinition definition in startup.ScriptedMods)
                {
                    ModMetaData metadata = definition.MetaData;
                    _logger.LogDebug("Mod loaded: {Name} v{Version} by {Author}", metadata.Name, metadata.Version, metadata.Author);
                }
                cancellationToken.ThrowIfCancellationRequested();
                Volatile.Write(ref _startupDiscovery, startup);
                return JobHandle.Completed;
            }
            catch
            {
                Volatile.Write(ref _scanStarted, 0);
                throw;
            }
        }
        catch (Exception error)
        {
            return JobHandle.FromException(error);
        }
    }

    public IEnumerable<ModDefinition> GetMods() => StartupDiscovery.ScriptedMods;

    internal ModDiscoveryResult StartupDiscovery => Volatile.Read(ref _startupDiscovery)
        ?? throw new InvalidOperationException("ModSet catalog must be prepared before use.");

    // Repeatable cold I/O. Does not log, publish, or read cooked payloads; refresh can reject Diagnostics.
    internal ModDiscoveryResult Discover(AssetTarget target, CancellationToken cancellationToken = default) =>
        DiscoverAt(ModsDirectoryPath, target, cancellationToken);

    internal string ModsDirectoryPath => _modsDirectory;

    internal static ModDiscoveryResult DiscoverAt(string modsDirectory, AssetTarget target, CancellationToken cancellationToken) =>
        DiscoverAt(modsDirectory, target, cancellationToken, legacyMetadataOnly: !OperatingSystem.IsLinux() && !OperatingSystem.IsWindows());

    internal static ModDiscoveryResult DiscoverAt(string modsDirectory, AssetTarget target, CancellationToken cancellationToken, bool legacyMetadataOnly)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string contentSide = target.ToCanonicalString();
        var diagnostics = new List<string>();
        var installed = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var candidates = new List<ModDefinition>();
        var scripted = new List<ModDefinition>();
        var serializer = new JsonSerializer();

        string[] entries = ReadModEntries(modsDirectory, diagnostics, out string? linkedRoot);
        foreach (string discovered in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string entry = linkedRoot is null ? discovered : Path.Combine(modsDirectory, Path.GetFileName(discovered));
            try
            {
                FileAttributes attributes = File.GetAttributes(discovered);
                bool linkedEntry = (attributes & FileAttributes.ReparsePoint) != 0;
                if ((attributes & FileAttributes.Device) != 0 || (linkedEntry && (attributes & FileAttributes.Directory) == 0))
                    throw new InvalidDataException("Expected a link to an existing mod directory or an ordinary mod directory.");
                if ((attributes & FileAttributes.Directory) == 0)
                    continue;

                bool linked = linkedEntry || linkedRoot is not null;
                string source = linkedEntry ? Directory.ResolveLinkTarget(discovered, returnFinalTarget: true)?.FullName
                    ?? throw new InvalidDataException("Cannot resolve the mod directory link.") : discovered;
                ModMetaData metadata = ReadMetadata(source, serializer, cancellationToken, legacyMetadataOnly);
                if (!installed.TryGetValue(metadata.Id, out var paths))
                    installed.Add(metadata.Id, paths = []);
                paths.Add(entry);

                var definition = new ModDefinition(metadata, entry);
                if (metadata.ContentOnly)
                {
                    if (linked)
                        throw new InvalidDataException("A contentOnly mod cannot use a linked directory.");
                    if (legacyMetadataOnly)
                        throw new InvalidDataException("A contentOnly mod requires platform support for safe cooked Content reads.");
                    RequireNoLuaEntryPoints(entry, metadata.Id);
                }
                else
                {
                    scripted.Add(definition);
                }

                if (!linked && !legacyMetadataOnly)
                    candidates.Add(definition);
                else if (ContentCaptureFiles.DirectoryExists(Path.Combine(source, "Content", contentSide)))
                    throw new InvalidDataException(linked
                        ? "Cooked Content cannot be captured from a linked mod directory."
                        : "Cooked Content cannot be captured on this platform.");
            }
            catch (Exception error) when (IsDiscoveryError(error))
            {
                diagnostics.Add($"Error loading mod '{entry}': {error.Message}");
            }
        }

        foreach (var (id, paths) in installed)
        {
            if (paths.Count > 1)
                diagnostics.Add($"Duplicate mod id '{id}' in '{string.Join("', '", paths.Order(StringComparer.Ordinal))}'.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        // For an ambiguous legacy install only, Lua keeps the first valid scripted folder in enumeration order.
        return new ModDiscoveryResult(
            Array.AsReadOnly(candidates.Where(mod => installed[mod.MetaData.Id].Count == 1)
                .OrderBy(mod => mod.MetaData.Id, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(installed.Keys.Order(StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(diagnostics.Order(StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(scripted.DistinctBy(mod => mod.MetaData.Id, StringComparer.Ordinal)
                .OrderBy(mod => mod.MetaData.Id, StringComparer.Ordinal).ToArray()), linkedRoot);
    }

    private static string[] ReadModEntries(string directory, List<string> diagnostics, out string? linkedRoot)
    {
        linkedRoot = null;
        try
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(directory); }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { attributes = 0; }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if ((attributes & FileAttributes.Directory) == 0 || !ContentCaptureFiles.DirectoryExists(Path.GetDirectoryName(directory)!))
                    throw new InvalidDataException("Expected a link to an existing Mods directory.");
                linkedRoot = Directory.ResolveLinkTarget(directory, returnFinalTarget: false)?.FullName
                    ?? throw new InvalidDataException("Cannot resolve the Mods directory link.");
            }

            string source = linkedRoot ?? directory;
            if (ContentCaptureFiles.DirectoryExists(source)) return Directory.GetFileSystemEntries(source);
            if (linkedRoot is not null) throw new InvalidDataException("Linked Mods directory is missing.");
            return [];
        }
        catch (Exception error) when (IsDiscoveryError(error))
        {
            diagnostics.Add($"Error reading mod directory '{directory}': {error.Message}");
            return [];
        }
    }

    private static ModMetaData ReadMetadata(string directory, JsonSerializer serializer, CancellationToken cancellationToken, bool legacyMetadataOnly)
    {
        string path = Path.Combine(directory, "mod_info.json");
        byte[] metadata = (legacyMetadataOnly ? ReadLegacyMetadata(path, cancellationToken) : ContentCaptureFiles.ReadMetadata(path, cancellationToken))
            ?? throw new InvalidDataException("Missing mod_info.json.");
        using var reader = new StreamReader(new MemoryStream(metadata, writable: false));
        using var json = new JsonTextReader(reader) { DateParseHandling = DateParseHandling.None };
        JObject document = JObject.Load(json);
        while (json.Read()) { } // Read through trailing comments; a second JSON value is invalid.
        if (document.GetValue("contentOnly", StringComparison.OrdinalIgnoreCase) is { Type: not JTokenType.Boolean })
            throw new InvalidDataException("contentOnly must be a JSON boolean.");
        ModMetaData result = document.ToObject<ModMetaData>(serializer);
        if (string.IsNullOrWhiteSpace(result.Id))
            throw new InvalidDataException("Invalid mod Id.");
        return result;
    }

    // Only for legacy Lua discovery on platforms without cooked capture; never produces a Content candidate.
    private static byte[]? ReadLegacyMetadata(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ContentCaptureFiles.DirectoryExists(Path.GetDirectoryName(path)!)) return null;
        var file = new FileInfo(path);
        FileAttributes attributes = file.Attributes;
        if (attributes == (FileAttributes)(-1)) return null;
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0 || file.Length == 0)
            throw new InvalidDataException($"Expected a nonempty ordinary mod metadata file: '{path}'.");
        using FileStream stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        const int limit = ContentCaptureFiles.MaximumMetadataBytes;
        long length = stream.Length;
        if (length > limit) throw new InvalidDataException($"Mod metadata exceeds {limit} bytes: '{path}'.");
        var bytes = new byte[(int)length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException($"Mod metadata changed while reading: '{path}'.");
        cancellationToken.ThrowIfCancellationRequested();
        return bytes;
    }

    private static void RequireNoLuaEntryPoints(string directory, string id)
    {
        foreach (string side in EntryPointDirectories)
        {
            string entryPoint = Path.Combine(directory, side, "main.lua");
            using var entry = ContentCaptureFiles.OpenRead(entryPoint);
            if (entry is not null)
                throw new InvalidDataException($"Mod '{id}' is marked contentOnly but has Lua entry point '{entryPoint}'.");
        }
    }

    private static bool IsDiscoveryError(Exception error) =>
        error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException;
}

internal sealed record ModDiscoveryResult(IReadOnlyList<ModDefinition> Candidates, IReadOnlyList<string> InstalledOwnerIds,
    IReadOnlyList<string> Diagnostics, IReadOnlyList<ModDefinition> ScriptedMods, string? LinkedRoot);
