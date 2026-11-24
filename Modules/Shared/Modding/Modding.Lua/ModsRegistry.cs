using System.Collections.Frozen;
using System.Collections.Immutable;
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
    private readonly IFileSystem? _fileSystem;
    private ModDiscoveryResult? _startupDiscovery;
    private ModStartupDependencies? _startupDependencies;
    private int _scanStarted;

    // assetsManager is retained for source compatibility; discovery never uses it.
    public ModsRegistry(IAssetsManager? assetsManager = null, ILogger<IModsRegistry>? logger = null, IFileSystem? fileSystem = null)
    {
        _logger = logger ?? NullLogger<IModsRegistry>.Instance;
        _fileSystem = fileSystem;
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

    public IEnumerable<ModDefinition> GetMods() => StartupDependencies is { } startup
        ? startup.ScriptedMods : StartupDiscovery.ScriptedMods;

    internal ModDiscoveryResult StartupDiscovery => Volatile.Read(ref _startupDiscovery)
        ?? throw new InvalidOperationException("ModSet catalog must be prepared before use.");

    internal ModStartupDependencies? StartupDependencies => Volatile.Read(ref _startupDependencies);

    internal ModStartupDependencies ResolveStartupDependencies(AssetTarget side,
        IReadOnlyDictionary<string, ModCookedAvailability> cooked, CancellationToken cancellationToken)
    {
        var discovery = StartupDiscovery;
        var scripted = discovery.ScriptedMods.ToDictionary(static mod => mod.MetaData.Id, StringComparer.Ordinal);
        string sideDirectory = side.ToCanonicalString();
        var nodes = new List<ModDependencyNode>(discovery.DeclaredMods.Count);
        foreach (string id in discovery.InstalledOwnerIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var declared = discovery.DeclaredMods[id];
            bool hasScript = scripted.TryGetValue(id, out var mod) &&
                (HasEntryPoint(mod.Directory, "Shared") || HasEntryPoint(mod.Directory, sideDirectory));
            nodes.Add(new ModDependencyNode(id, declared.Definition.MetaData.Version, declared.Dependencies,
                hasScript, cooked.GetValueOrDefault(id)));
        }
        var resolution = ModDependencyResolver.Resolve(nodes, side, cancellationToken);
        var orderedScripts = resolution.OrderedIds.Where(scripted.ContainsKey).Select(id => scripted[id] with
        {
            ResolvedDirectDependencies = resolution.DirectTargetsById[id]
        }).ToImmutableArray();
        return new ModStartupDependencies(nodes.ToFrozenDictionary(static node => node.Id, StringComparer.Ordinal),
            resolution, orderedScripts);
    }

    internal void PublishStartupDependencies(ModStartupDependencies startup) => Volatile.Write(ref _startupDependencies, startup);

    private bool HasEntryPoint(string directory, string side) => _fileSystem is { } fileSystem
        ? fileSystem.Exists(fileSystem.Combine(directory, side, "main.lua"))
        : File.Exists(Path.Combine(directory, side, "main.lua"));

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
        var declarations = new Dictionary<string, DiscoveredMod>(StringComparer.Ordinal);
        var duplicatePaths = new Dictionary<string, List<string>>(StringComparer.Ordinal);
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
                var (metadata, dependencies) = ReadMetadata(source, serializer, cancellationToken, legacyMetadataOnly);
                var definition = new ModDefinition(metadata, entry);
                var declared = new DiscoveredMod(definition, dependencies, source);
                if (!declarations.TryAdd(metadata.Id, declared))
                {
                    if (!duplicatePaths.TryGetValue(metadata.Id, out var paths))
                        duplicatePaths.Add(metadata.Id, paths = [declarations[metadata.Id].Definition.Directory]);
                    paths.Add(entry);
                    continue;
                }
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
                {
                    candidates.Add(definition);
                    declarations[metadata.Id] = declared with { UncapturableCookedSource = null };
                }
                else if (ContentCaptureFiles.DirectoryExists(Path.Combine(source, "Content", contentSide)))
                    throw new InvalidDataException(linked
                        ? "Cooked Content cannot be captured from a linked mod directory."
                        : "Cooked Content cannot be captured on this platform.");
            }
            catch (ModDependencyException error)
            {
                throw new ModDependencyException($"Error loading mod '{entry}' on {contentSide}: {error.Message}");
            }
            catch (Exception error) when (IsDiscoveryError(error))
            {
                diagnostics.Add($"Error loading mod '{entry}': {error.Message}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (duplicatePaths.Count != 0)
        {
            var duplicates = duplicatePaths.OrderBy(static item => item.Key, StringComparer.Ordinal)
                .Select(item => $"Duplicate mod id '{item.Key}' on {contentSide} in '{string.Join("', '", item.Value.Order(StringComparer.Ordinal))}'.");
            throw new ModDependencyException(string.Join(Environment.NewLine, duplicates));
        }

        return new ModDiscoveryResult(
            Array.AsReadOnly(candidates.OrderBy(mod => mod.MetaData.Id, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(declarations.Keys.Order(StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(diagnostics.Order(StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(scripted.OrderBy(mod => mod.MetaData.Id, StringComparer.Ordinal).ToArray()), linkedRoot,
            declarations.ToFrozenDictionary(StringComparer.Ordinal));
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

    private static (ModMetaData, ModDependencyManifest) ReadMetadata(string directory, JsonSerializer serializer,
        CancellationToken cancellationToken, bool legacyMetadataOnly)
    {
        string path = Path.Combine(directory, "mod_info.json");
        byte[] metadata = (legacyMetadataOnly ? ReadLegacyMetadata(path, cancellationToken) : ContentCaptureFiles.ReadMetadata(path, cancellationToken))
            ?? throw new InvalidDataException("Missing mod_info.json.");
        using var reader = new StreamReader(new MemoryStream(metadata, writable: false));
        using var json = new DependencyMetadataReader(reader) { DateParseHandling = DateParseHandling.None };
        JObject document;
        try { document = JObject.Load(json); }
        catch (JsonReaderException error)
        {
            if (json.DuplicatePath is { } observedDuplicate)
                throw DuplicateProperty(observedDuplicate);
            if (json.ReadingDependency)
                throw new ModDependencyException($"Mod '{json.ObservedId ?? "<unknown>"}': Malformed 'dependencies' JSON in '{path}': {error.Message}");
            if (json.SawDependencyProperty)
                _ = ModDependencyManifest.Parse(json.ObservedId ?? "<unknown>", ReadCompletedDependency(metadata));
            throw;
        }
        if (json.DuplicatePath is { } duplicate)
            throw DuplicateProperty(duplicate);

        ModMetaData result;
        try
        {
            if (document.GetValue("contentOnly", StringComparison.OrdinalIgnoreCase) is { Type: not JTokenType.Boolean })
                throw new InvalidDataException("contentOnly must be a JSON boolean.");
            result = document.ToObject<ModMetaData>(serializer);
            if (string.IsNullOrWhiteSpace(result.Id))
                throw new InvalidDataException("Invalid mod Id.");
        }
        catch (Exception error) when (IsDiscoveryError(error))
        {
            // A simultaneous legacy warning must not hide an explicit dependency contract error.
            _ = ModDependencyManifest.Parse(json.ObservedId ?? "<unknown>", document["dependencies"]);
            throw;
        }
        var dependencies = ModDependencyManifest.Parse(result.Id, document["dependencies"]);
        while (json.Read()) { } // Reject trailing values without masking an explicit dependency error.
        return (result, dependencies);

        ModDependencyException DuplicateProperty(string propertyPath) =>
            new($"Mod '{json.ObservedId ?? "<unknown>"}': Duplicate dependency property at '{propertyPath}' in '{path}'.");
    }

    // The first reader already completed this value. Replay only the bounded in-memory prefix,
    // not the malformed legacy suffix, to recover the subtree that JObject.Load could not return.
    private static JToken? ReadCompletedDependency(byte[] metadata)
    {
        using var json = new JsonTextReader(new StreamReader(new MemoryStream(metadata, writable: false)))
            { DateParseHandling = DateParseHandling.None };
        while (json.Read())
        {
            if (json.Depth != 1 || json.TokenType != JsonToken.PropertyName || (string?)json.Value != "dependencies")
                continue;
            while (json.Read() && json.TokenType == JsonToken.Comment) { }
            return JToken.ReadFrom(json);
        }
        return null;
    }

    // JObject replaces repeated names before the contract parser sees them. Watch only the exact
    // new root property and its subtree, leaving Newtonsoft's legacy handling elsewhere intact.
    private sealed class DependencyMetadataReader : JsonTextReader
    {
        private const string DependencyName = "dependencies";
        private List<HashSet<string>?>? _objectFields;
        private bool _insideDependencyValue;
        private bool _startingDependencyProperty;
        private bool _isRootId;
        public string? DuplicatePath { get; private set; }
        public string? ObservedId { get; private set; }
        public bool SawDependencyProperty { get; private set; }
        public bool ReadingDependency => _startingDependencyProperty || _insideDependencyValue;

        public DependencyMetadataReader(TextReader reader) : base(reader) => PropertyNameTable = new RootDependencyNameTable(this);

        public override bool Read()
        {
            _startingDependencyProperty = false;
            if (!base.Read()) return false;
            bool atRootMember = Depth == 1;
            JsonToken token = TokenType;
            if (atRootMember && token == JsonToken.PropertyName)
            {
                string name = (string)Value!;
                _isRootId = string.Equals(name, "id", StringComparison.OrdinalIgnoreCase);
                _insideDependencyValue = name == DependencyName;
                if (_insideDependencyValue)
                {
                    if (SawDependencyProperty) DuplicatePath ??= DependencyName;
                    SawDependencyProperty = true;
                }
            }
            else if (_insideDependencyValue)
            {
                TrackDependencyFields(token);
                // A root-level scalar or closing token finishes the member; comments do not.
                if (atRootMember && token is not (JsonToken.StartObject or JsonToken.StartArray or JsonToken.StartConstructor or JsonToken.Comment))
                    _insideDependencyValue = false;
            }
            else if (_isRootId && atRootMember && token == JsonToken.String)
            {
                ObservedId = (string?)Value;
            }
            return true;
        }

        private void TrackDependencyFields(JsonToken token)
        {
            if (DuplicatePath is not null) return;
            switch (token)
            {
                case JsonToken.StartObject:
                    (_objectFields ??= []).Add(null);
                    break;
                case JsonToken.EndObject:
                    _objectFields!.RemoveAt(_objectFields.Count - 1);
                    break;
                case JsonToken.PropertyName:
                    var fields = _objectFields![^1] ??= new HashSet<string>(StringComparer.Ordinal);
                    if (!fields.Add((string)Value!)) DuplicatePath = Path;
                    break;
            }
        }

        // Json.NET 13.0.4 supplies the decoded property name to the name table before testing for ':'.
        // Depth still describes the last emitted token; a StartObject token excludes its own level.
        private sealed class RootDependencyNameTable(DependencyMetadataReader owner) : JsonNameTable
        {
            public override string? Get(char[] key, int start, int length)
            {
                if (length != DependencyName.Length || owner.Depth != (owner.TokenType == JsonToken.StartObject ? 0 : 1) ||
                    !key.AsSpan(start, length).SequenceEqual(DependencyName)) return null;
                owner._startingDependencyProperty = true;
                return DependencyName;
            }
        }
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
    IReadOnlyList<string> Diagnostics, IReadOnlyList<ModDefinition> ScriptedMods, string? LinkedRoot,
    FrozenDictionary<string, DiscoveredMod> DeclaredMods);

internal readonly record struct DiscoveredMod(ModDefinition Definition, ModDependencyManifest Dependencies, string? UncapturableCookedSource);

internal sealed record ModStartupDependencies(FrozenDictionary<string, ModDependencyNode> NodesById,
    ModDependencyResolution Resolution, ImmutableArray<ModDefinition> ScriptedMods);
