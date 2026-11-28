using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Composition;
using System.Security.Cryptography;
using Karpik.Content.Core;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonDocument = System.Text.Json.JsonDocument;
using JsonValueKind = System.Text.Json.JsonValueKind;

namespace Karpik.Engine.Shared.Modding.Lua;

[Export(typeof(IModsRegistry))]
[ServiceRegistration(ModuleScope.ModSet, ServiceLifetime.Singleton)]
public class ModsRegistry : IModsRegistry
{
    private static readonly string[] EntryPointDirectories = ["Shared", "Client", "Server"];
    private readonly string _modsDirectory;
    private readonly IFileSystem? _fileSystem;
    private readonly ILogger<IModsRegistry> _logger;
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
                FrozenSet<string> disabledIds = ReadDisabledIds(cancellationToken);
                ModDiscoveryResult startup = DiscoverAt(_modsDirectory, target, cancellationToken, disabledIds);
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
        IReadOnlyDictionary<string, ModCookedAvailability> cooked, CancellationToken cancellationToken) =>
        Compose(side, StartupDiscovery.DeclaredMods, cooked, cancellationToken, _fileSystem);

    private static ModStartupDependencies Compose(AssetTarget side, IReadOnlyDictionary<string, DiscoveredMod> selected,
        IReadOnlyDictionary<string, ModCookedAvailability> cooked, CancellationToken cancellationToken, IFileSystem? legacyFileSystem = null)
    {
        var budget = new LuaSourceCapture.Budget();
        var packages = new Dictionary<string, DiscoveredMod>(selected.Count, StringComparer.Ordinal);
        var nodes = new List<ModDependencyNode>(selected.Count);
        var executionSide = side == AssetTarget.Client ? ExecutionSide.Client : ExecutionSide.Server;
        // Charge survivor sources first so fresh captures cannot temporarily exceed the candidate budget.
        foreach (var (id, package) in selected.OrderBy(static item => item.Value.Definition.ScriptSource is null)
                     .ThenBy(static item => item.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mod = package.Definition;
            if (LuaSourceCapture.IsSupported)
            {
                if (mod.MetaData.ContentOnly && mod.ResolvedDirectDependencies is null)
                    VerifyManifest(package, LuaSourceCapture.ResolveRoot(mod.Directory), cancellationToken);
                mod = budget.Pin(mod, executionSide, cancellationToken,
                    verifyManifest: root => VerifyManifest(package, root, cancellationToken));
                if (!package.ExpectedLuaEntries.IsDefault && mod.ScriptSource is { } fresh)
                    foreach (string path in package.ExpectedLuaEntries)
                        if (!fresh.TryGetFile(path, out _))
                            throw new InvalidDataException($"Selected Lua entry point '{Path.Combine(mod.Directory, path)}' is missing.");
            }
            bool hasScript = mod.ScriptSource is { } source ? LuaSourceCapture.HasEntryPoint(source) :
                !LuaSourceCapture.IsSupported && !mod.MetaData.ContentOnly &&
                (LegacyHasEntryPoint(legacyFileSystem, mod.Directory, "Shared") ||
                 LegacyHasEntryPoint(legacyFileSystem, mod.Directory, LuaSourceCapture.SideDirectory(executionSide)));
            packages.Add(id, package with { Definition = mod });
            nodes.Add(new ModDependencyNode(id, mod.MetaData.Version, package.Dependencies, hasScript, cooked.GetValueOrDefault(id)));
        }
        var resolution = ModDependencyResolver.Resolve(nodes, side, cancellationToken);
        var scripted = ImmutableArray.CreateBuilder<ModDefinition>();
        foreach (string id in resolution.OrderedIds)
        {
            var package = packages[id];
            var mod = package.Definition with { ResolvedDirectDependencies = resolution.DirectTargetsById[id] };
            packages[id] = package with { Definition = mod };
            if (!mod.MetaData.ContentOnly) scripted.Add(mod);
        }
        return new ModStartupDependencies(side, nodes.ToFrozenDictionary(static node => node.Id, StringComparer.Ordinal),
            resolution, scripted.ToImmutable(), packages.ToFrozenDictionary(StringComparer.Ordinal));
    }

    private static bool LegacyHasEntryPoint(IFileSystem? fs, string directory, string side) => fs is not null
        ? fs.Exists(fs.Combine(directory, side, "main.lua")) : File.Exists(Path.Combine(directory, side, "main.lua"));

    private static void VerifyManifest(DiscoveredMod package, string root, CancellationToken cancellationToken)
    {
        string path = Path.Combine(root, "mod_info.json");
        if (!LuaSourceCapture.PhysicalPathComparer.Equals(root, package.SourceDirectory) ||
            !string.Equals(package.ManifestIdentity, ReadManifestIdentity(path, cancellationToken), StringComparison.Ordinal))
            throw new InvalidDataException($"Mod manifest changed or disappeared during capture: '{package.Definition.Directory}'.");
    }

    private static string? ReadManifestIdentity(string path, CancellationToken cancellationToken) =>
        (LegacyMetadataOnly ? ReadLegacyMetadata(path, cancellationToken) : ContentCaptureFiles.ReadMetadata(path, cancellationToken)) is { } bytes
            ? Convert.ToHexString(SHA256.HashData(bytes)) : null;

    internal void PublishStartupDependencies(ModStartupDependencies startup) => Volatile.Write(ref _startupDependencies, startup);

    private static bool LegacyMetadataOnly => !OperatingSystem.IsLinux() && !OperatingSystem.IsWindows();

    private FrozenSet<string> ReadDisabledIds(CancellationToken cancellationToken)
    {
        string path = Path.Combine(Path.GetDirectoryName(_modsDirectory)!, "mods.json");
        try
        {
            // Check for absence before requiring ordinary path components, as with the legacy no-config startup.
            try { _ = File.GetAttributes(path); }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
            {
                return FrozenSet<string>.Empty;
            }

            byte[] bytes = (LegacyMetadataOnly ? ReadLegacyMetadata(path, cancellationToken) : ContentCaptureFiles.ReadMetadata(path, cancellationToken))
                ?? throw new InvalidDataException("The selection file disappeared while reading.");
            int offset = bytes.AsSpan().StartsWith("\uFEFF"u8) ? 3 : 0;
            using var json = JsonDocument.Parse(bytes.AsMemory(offset));
            if (json.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Expected a JSON object containing only the 'disabled' array.");
            var properties = json.RootElement.EnumerateObject();
            if (!properties.MoveNext() || !properties.Current.NameEquals("disabled") ||
                properties.Current.Value.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Expected a JSON object containing only the 'disabled' array.");
            var disabled = properties.Current.Value;
            if (properties.MoveNext())
                throw new InvalidDataException("Only one 'disabled' property is allowed.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in disabled.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.ValueKind != JsonValueKind.String || entry.GetString() is not { } id || string.IsNullOrWhiteSpace(id))
                    throw new InvalidDataException("Each 'disabled' entry must be a nonblank mod ID string.");
                ids.Add(id);
            }
            return ids.ToFrozenSet(StringComparer.Ordinal);
        }
        catch (Exception error) when (IsDiscoveryError(error) || error is System.Text.Json.JsonException or InvalidOperationException)
        {
            throw new InvalidDataException($"Invalid mod selection at '{path}': {error.Message}", error);
        }
    }

    // Repeatable cold I/O. Does not log, publish, or read cooked payloads; refresh can reject Diagnostics.
    internal ModDiscoveryResult Discover(AssetTarget target, CancellationToken cancellationToken = default) =>
        DiscoverAt(ModsDirectoryPath, target, cancellationToken);

    internal ModSelection DiscoverSelection(ModStartupDependencies basis, IReadOnlyCollection<string> selectedIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!LuaSourceCapture.IsSupported) throw new PlatformNotSupportedException("Live mod selection requires verified Lua source capture.");
        if (selectedIds.Count > LuaSourceCapture.MaximumPackages)
            throw new InvalidDataException($"Selected mod set exceeds {LuaSourceCapture.MaximumPackages} packages.");
        var packages = new Dictionary<string, DiscoveredMod>(StringComparer.Ordinal);
        var newIds = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var acceptedPaths = new HashSet<string>(LuaSourceCapture.PhysicalPathComparer);
        foreach (string id in selectedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                throw new InvalidDataException($"Invalid or duplicate selected mod ID '{id}'.");
            if (basis.PackagesById.TryGetValue(id, out var accepted))
            {
                packages.Add(id, accepted);
                acceptedPaths.Add(accepted.Definition.Directory);
                acceptedPaths.Add(accepted.SourceDirectory);
            }
            else newIds.Add(id);
        }
        if (newIds.Count != 0)
        {
            var discovered = DiscoverCore(_modsDirectory, basis.Side, cancellationToken, LegacyMetadataOnly,
                selectedIds: newIds, skipPaths: acceptedPaths);
            foreach (string id in newIds.Order(StringComparer.Ordinal))
            {
                if (!discovered.DeclaredMods.TryGetValue(id, out var package) || discovered.InvalidPaths.Contains(package.Definition.Directory))
                    throw new InvalidDataException($"Selected mod '{id}' is missing or invalid on {basis.Side}. {string.Join("; ", discovered.Diagnostics)}");
                if (!package.Definition.MetaData.ContentOnly)
                {
                    var side = basis.Side == AssetTarget.Client ? ExecutionSide.Client : ExecutionSide.Server;
                    package = package with { ExpectedLuaEntries = LuaSourceCapture.ObserveEntryPoints(package.SourceDirectory, side) };
                }
                packages.Add(id, package);
            }
        }
        var cookedSurvivors = packages.Keys.Where(id => basis.NodesById.TryGetValue(id, out var node) &&
            node.CookedAvailability == ModCookedAvailability.AdmittedCandidate).ToFrozenSet(StringComparer.Ordinal);
        return new ModSelection(basis.Side, packages.ToFrozenDictionary(StringComparer.Ordinal),
            newIds.ToFrozenSet(StringComparer.Ordinal), cookedSurvivors);
    }

    internal static ModStartupDependencies ComposeSelection(ModSelection selection, IReadOnlyCollection<string> newlyAcceptedCookedIds,
        CancellationToken cancellationToken = default)
    {
        if (!LuaSourceCapture.IsSupported) throw new PlatformNotSupportedException("Live mod selection requires verified Lua source capture.");
        var cooked = selection.AcceptedCookedSurvivors.ToDictionary(static id => id,
            static _ => ModCookedAvailability.AdmittedCandidate, StringComparer.Ordinal);
        foreach (string id in newlyAcceptedCookedIds)
        {
            if (!selection.NewIds.Contains(id) || !cooked.TryAdd(id, ModCookedAvailability.AdmittedCandidate))
                throw new InvalidDataException($"Cooked Content '{id}' is not a newly selected package.");
        }
        return Compose(selection.Side, selection.PackagesById, cooked, cancellationToken);
    }

    internal string ModsDirectoryPath => _modsDirectory;

    internal static ModDiscoveryResult DiscoverAt(string modsDirectory, AssetTarget target, CancellationToken cancellationToken,
        FrozenSet<string>? disabledIds = null) =>
        DiscoverAt(modsDirectory, target, cancellationToken, LegacyMetadataOnly, disabledIds);

    internal static ModDiscoveryResult DiscoverAt(string modsDirectory, AssetTarget target, CancellationToken cancellationToken,
        bool legacyMetadataOnly, FrozenSet<string>? disabledIds = null) =>
        DiscoverCore(modsDirectory, target, cancellationToken, legacyMetadataOnly, disabledIds);

    internal ModDiscoveryResult DiscoverInstalled(AssetTarget target, CancellationToken cancellationToken = default) =>
        DiscoverCore(_modsDirectory, target, cancellationToken, LegacyMetadataOnly, inventoryOnly: true);

    private static ModDiscoveryResult DiscoverCore(string modsDirectory, AssetTarget target, CancellationToken cancellationToken,
        bool legacyMetadataOnly, FrozenSet<string>? disabledIds = null, HashSet<string>? selectedIds = null,
        HashSet<string>? skipPaths = null, bool inventoryOnly = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string contentSide = target.ToCanonicalString();
        var diagnostics = new List<string>();
        var invalidPaths = new HashSet<string>(LuaSourceCapture.PhysicalPathComparer);
        var declarations = new Dictionary<string, DiscoveredMod>(StringComparer.Ordinal);
        var firstPaths = new Dictionary<string, string>(StringComparer.Ordinal);
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
                if (skipPaths is not null && (skipPaths.Contains(entry) || skipPaths.Contains(source))) continue;
                var manifest = ReadMetadata(source, serializer, cancellationToken, legacyMetadataOnly,
                    disabledIds, selectedIds, inventoryOnly, out string id, out string? manifestIdentity);
                if (selectedIds is not null && !selectedIds.Contains(id)) continue;
                if (!firstPaths.TryAdd(id, entry))
                {
                    if (!duplicatePaths.TryGetValue(id, out var paths))
                        duplicatePaths.Add(id, paths = [firstPaths[id]]);
                    paths.Add(entry);
                    continue;
                }
                if (manifest is not { } active) continue;
                var (metadata, dependencies) = active;
                var definition = new ModDefinition(metadata, entry);
                var declared = new DiscoveredMod(definition, dependencies, source, manifestIdentity!, source);
                declarations.Add(id, declared);
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
            catch (ModDependencyException error) when (!inventoryOnly && selectedIds is null)
            {
                throw new ModDependencyException($"Error loading mod '{entry}' on {contentSide}: {error.Message}");
            }
            catch (Exception error) when (IsDiscoveryError(error) || error is ModDependencyException)
            {
                invalidPaths.Add(entry);
                diagnostics.Add($"Error loading mod '{entry}': {error.Message}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (duplicatePaths.Count != 0)
        {
            var duplicates = duplicatePaths.OrderBy(static item => item.Key, StringComparer.Ordinal)
                .Select(item => $"Duplicate mod id '{item.Key}' on {contentSide} in '{string.Join("', '", item.Value.Order(StringComparer.Ordinal))}'.");
            if (!inventoryOnly) throw new ModDependencyException(string.Join(Environment.NewLine, duplicates));
            diagnostics.AddRange(duplicates);
        }

        return new ModDiscoveryResult(
            Array.AsReadOnly(candidates.OrderBy(mod => mod.MetaData.Id, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly((inventoryOnly ? firstPaths.Keys.AsEnumerable() : declarations.Keys).Order(StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(diagnostics.Order(StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(scripted.OrderBy(mod => mod.MetaData.Id, StringComparer.Ordinal).ToArray()), linkedRoot,
            declarations.ToFrozenDictionary(StringComparer.Ordinal), disabledIds ?? FrozenSet<string>.Empty,
            firstPaths.ToFrozenDictionary(StringComparer.Ordinal), invalidPaths.ToFrozenSet(LuaSourceCapture.PhysicalPathComparer));
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

    private static (ModMetaData, ModDependencyManifest)? ReadMetadata(string directory, JsonSerializer serializer,
        CancellationToken cancellationToken, bool legacyMetadataOnly, FrozenSet<string>? disabledIds,
        HashSet<string>? selectedIds, bool inventoryOnly, out string id, out string? manifestIdentity)
    {
        string path = Path.Combine(directory, "mod_info.json");
        manifestIdentity = null;
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
        JsonReaderException? trailingError = null;
        string? manifestId = ReadManifestId(document);
        if (inventoryOnly && string.IsNullOrWhiteSpace(manifestId)) throw new InvalidDataException("Invalid mod Id.");
        if (manifestId is not null && (inventoryOnly || disabledIds?.Contains(manifestId) == true ||
            (selectedIds is not null && !selectedIds.Contains(manifestId))))
        {
            try
            {
                while (json.Read()) { }
                id = manifestId;
                return null;
            }
            catch (JsonReaderException error)
            {
                // A syntactically broken manifest still follows the existing error precedence.
                trailingError = error;
            }
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
        if (trailingError is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(trailingError);
        while (json.Read()) { } // Reject trailing values without masking an explicit dependency error.
        id = result.Id;
        manifestIdentity = Convert.ToHexString(SHA256.HashData(metadata));
        return (result, dependencies);

        ModDependencyException DuplicateProperty(string propertyPath) =>
            new($"Mod '{json.ObservedId ?? "<unknown>"}': Duplicate dependency property at '{propertyPath}' in '{path}'.");
    }

    private static string? ReadManifestId(JObject document)
    {
        // Keep legacy case-insensitive property binding and scalar-to-string conversion.
        JToken? id = null;
        foreach (var property in document.Properties())
            if (string.Equals(property.Name, "id", StringComparison.OrdinalIgnoreCase)) id = property.Value;
        return id?.Type is JTokenType.String or JTokenType.Boolean or JTokenType.Integer or JTokenType.Float
            ? (string?)id : null;
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
    FrozenDictionary<string, DiscoveredMod> DeclaredMods, FrozenSet<string> DisabledIds,
    FrozenDictionary<string, string> InstalledPaths, FrozenSet<string> InvalidPaths);

internal readonly record struct DiscoveredMod(ModDefinition Definition, ModDependencyManifest Dependencies,
    string? UncapturableCookedSource, string ManifestIdentity, string SourceDirectory, ImmutableArray<string> ExpectedLuaEntries = default);

internal sealed record ModSelection(AssetTarget Side, FrozenDictionary<string, DiscoveredMod> PackagesById,
    FrozenSet<string> NewIds, FrozenSet<string> AcceptedCookedSurvivors);

// Startup is the initial composition; Simulation owns subsequent accepted revisions.
internal sealed record ModStartupDependencies(AssetTarget Side, FrozenDictionary<string, ModDependencyNode> NodesById,
    ModDependencyResolution Resolution, ImmutableArray<ModDefinition> ScriptedMods,
    FrozenDictionary<string, DiscoveredMod> PackagesById)
{
    internal ModStartupDependencies AcceptCooked(IReadOnlySet<string> acceptedIds) => this with
    {
        NodesById = NodesById.ToFrozenDictionary(static pair => pair.Key, pair => pair.Value with
        {
            CookedAvailability = acceptedIds.Contains(pair.Key) ? ModCookedAvailability.AdmittedCandidate : ModCookedAvailability.Absent
        }, StringComparer.Ordinal)
    };

    internal ModStartupDependencies WithScriptDefinitions(IReadOnlyList<ModDefinition> definitions)
    {
        var packages = PackagesById.ToDictionary(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            string id = definition.MetaData.Id;
            packages[id] = packages[id] with { Definition = definition };
        }
        return this with
        {
            ScriptedMods = definitions.ToImmutableArray(),
            PackagesById = packages.ToFrozenDictionary(StringComparer.Ordinal)
        };
    }
}
