using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Karpik.Content.Core;
using Karpik.Content.Runtime;

namespace Karpik.Engine.Shared.Modding.Lua;

internal sealed class LuaModContentRefresh
{
    private readonly string _modsDirectory;
    private readonly AssetTarget _target;
    private readonly ModStartupDependencies _startup;
    private readonly FrozenSet<string> _disabledIds;
    private readonly Dictionary<string, string> _scripted;
    // Only the coordinator's committed package list decides which entries survive a refresh.
    private readonly ConditionalWeakTable<ModContentPackage, PackageDependencies> _dependencies = new();

    public LuaModContentRefresh(string modsDirectory, AssetTarget target, ModStartupDependencies startup,
        IReadOnlyList<ModContentPackage> startupPackages, FrozenSet<string> disabledIds)
    {
        _modsDirectory = modsDirectory;
        _target = target;
        _startup = startup;
        _disabledIds = disabledIds;
        _scripted = startup.ScriptedMods.ToDictionary(static mod => mod.MetaData.Id, static mod => mod.Directory, StringComparer.Ordinal);
        foreach (var package in startupPackages)
        {
            if (_scripted.ContainsKey(package.ModId)) continue;
            var node = startup.NodesById[package.ModId];
            _dependencies.Add(package, new PackageDependencies(node.Version, node.Dependencies));
        }
    }

    public async Task<(IReadOnlyList<ModContentPackage>, IReadOnlyCollection<string>, IReadOnlyList<ModContentCapture>)> CaptureAsync(
        IReadOnlyList<ModContentPackage> previous, Action<ContentDiagnostic> report, CancellationToken cancellation)
    {
        string modsDirectory = _modsDirectory;
        AssetTarget target = _target;
        var scripted = _scripted;
        ModDiscoveryResult discovery = ModsRegistry.DiscoverAt(modsDirectory, target, cancellation, _disabledIds);
        if (discovery.Diagnostics.Count != 0)
            throw new InvalidDataException(string.Join(Environment.NewLine, discovery.Diagnostics));

        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var activeScriptPaths = scripted.Values.ToHashSet(pathComparer);
        var currentPaths = discovery.Candidates.Select(static mod => mod.Directory).ToHashSet(pathComparer);
        var owners = discovery.InstalledOwnerIds.ToHashSet(StringComparer.Ordinal);
        owners.UnionWith(scripted.Keys);
        var previousById = previous.ToDictionary(static package => package.ModId, StringComparer.Ordinal);
        var selected = new Dictionary<string, ModContentPackage>(StringComparer.Ordinal);
        var activeSeen = new HashSet<string>(StringComparer.Ordinal);
        var captures = new List<ModContentCapture>();
        try
        {
            // Legacy Lua-only links can be installed without being eligible for Content capture.
            foreach (var mod in discovery.ScriptedMods)
            {
                if (!currentPaths.Add(mod.Directory)) continue;
                string id = mod.MetaData.Id;
                if (scripted.TryGetValue(id, out var activePath))
                {
                    if (pathComparer.Equals(activePath, mod.Directory) && !previousById.ContainsKey(id))
                        activeSeen.Add(id);
                }
                else KeepPriorUntilRestart(id, mod.Directory);
            }

            // An otherwise unrecognized replacement of a known folder by a file/link is not a removal.
            foreach (var package in previous)
            {
                string path = ModFolder(package);
                if (!currentPaths.Contains(path)) ValidateUnlistedFolder(path);
            }
            foreach (var mod in discovery.Candidates)
            {
                cancellation.ThrowIfCancellationRequested();
                string id = mod.MetaData.Id;
                previousById.TryGetValue(id, out var prior);
                bool active = scripted.TryGetValue(id, out string? activePath);
                if (active)
                {
                    if (mod.MetaData.ContentOnly || !pathComparer.Equals(activePath, mod.Directory)) continue;
                    activeSeen.Add(id);
                }
                else if (activeScriptPaths.Contains(mod.Directory) || !mod.MetaData.ContentOnly)
                {
                    KeepPriorUntilRestart(id, mod.Directory);
                    continue;
                }

                var capture = await ModContentCapture.CaptureAsync(mod.Directory, id, target, prior, report, cancellation)
                    .ConfigureAwait(false);
                if (capture is not null)
                {
                    captures.Add(capture);
                    selected.Add(id, active ? capture.Package : AttachDependencies(capture.Package, discovery.DeclaredMods[id]));
                }
                else if (prior is not null && pathComparer.Equals(ModFolder(prior), mod.Directory))
                {
                    throw new InvalidDataException($"Previously accepted {target} Content for mod '{id}' is missing from '{mod.Directory}'.");
                }
                // A new install may have no package for this side; the discovered folder must still exist.
                if (!ContentCaptureFiles.DirectoryExists(mod.Directory))
                    throw new InvalidDataException($"Mod '{id}' disappeared during Content refresh: '{mod.Directory}'.");
            }
            foreach (var (id, path) in scripted)
            {
                if (activeSeen.Contains(id)) continue;
                if (!currentPaths.Contains(path)) ValidateUnlistedFolder(path);
                RestartRequired(id, path);
                if (previousById.TryGetValue(id, out var package)) selected.Add(id, package);
            }
            return (ResolveOrder(selected, cancellation), owners, captures);
        }
        catch (Exception error)
        {
            ModContentCapture.DisposeAll(captures, error);
            throw;
        }

        // The Lua identity is lexical. For a known linked root, classify an unlisted child against
        // its observed ordinary source to distinguish removal from a file replacing the folder.
        void ValidateUnlistedFolder(string path) => ContentCaptureFiles.DirectoryExists(discovery.LinkedRoot is string source
            ? Path.Combine(source, Path.GetFileName(path)) : path);

        void KeepPriorUntilRestart(string id, string path)
        {
            RestartRequired(id, path);
            // A still-installed content-only folder that became scripted isn't an uninstall.
            if (previousById.TryGetValue(id, out var prior) && pathComparer.Equals(ModFolder(prior), path))
                selected.Add(id, prior);
        }

        void RestartRequired(string id, string path) => report(new ContentDiagnostic("KCR-MOD-RESTART",
            ContentDiagnosticSeverity.Warning, path, $"Restart required to change scripted mod '{id}'."));
    }

    internal static async Task<(IReadOnlyList<ModContentPackage>, IReadOnlyCollection<string>, IReadOnlyList<ModContentCapture>)> CaptureLiveAsync(
        ModStartupDependencies basis, IReadOnlyList<ModContentPackage> previous, Action<ContentDiagnostic> report, CancellationToken cancellation)
    {
        var previousById = IndexAcceptedPackages(basis, previous);
        var selected = new Dictionary<string, ModContentPackage>(StringComparer.Ordinal);
        var captures = new List<ModContentCapture>();
        try
        {
            foreach (string id in basis.Resolution.OrderedIds)
            {
                cancellation.ThrowIfCancellationRequested();
                var accepted = basis.PackagesById[id];
                string path = accepted.Definition.Directory;
                previousById.TryGetValue(id, out var prior);
                // A disappeared install does not deselect an accepted mod. Also, Lua-only links cannot supply cooked assets.
                if (accepted.UncapturableCookedSource is not null || !ContentCaptureFiles.DirectoryExists(path))
                {
                    if (prior is not null) selected.Add(id, prior);
                    continue;
                }

                var capture = await ModContentCapture.CaptureAsync(path, id, basis.Side, prior, report, cancellation).ConfigureAwait(false);
                if (capture is not null)
                {
                    captures.Add(capture);
                    selected.Add(id, capture.Package);
                }
                else if (prior is not null)
                    throw new InvalidDataException($"Previously accepted {basis.Side} Content for mod '{id}' is missing from '{path}'.");
                if (!ContentCaptureFiles.DirectoryExists(path))
                    throw new InvalidDataException($"Mod '{id}' disappeared during Content refresh: '{path}'.");
            }

            // Live metadata stays pinned. A new cooked provider may still change Lua availability; require Apply for that.
            var nodes = basis.NodesById.Values.Select(node => node with
            {
                CookedAvailability = selected.ContainsKey(node.Id) ? ModCookedAvailability.AdmittedCandidate : ModCookedAvailability.Absent
            }).ToArray();
            var resolution = ModDependencyResolver.Resolve(nodes, basis.Side, cancellation);
            ValidateFrozenLua(basis, resolution, cancellation);
            return (resolution.OrderedIds.Where(selected.ContainsKey).Select(id => selected[id]).ToArray(), basis.PackagesById.Keys, captures);
        }
        catch (Exception error)
        {
            ModContentCapture.DisposeAll(captures, error);
            throw;
        }
    }

    internal static Dictionary<string, ModContentPackage> IndexAcceptedPackages(ModStartupDependencies basis, IReadOnlyList<ModContentPackage> previous)
    {
        var packages = new Dictionary<string, ModContentPackage>(previous.Count, StringComparer.Ordinal);
        foreach (var package in previous)
        {
            if (!basis.PackagesById.ContainsKey(package.ModId))
                throw new InvalidDataException($"Committed {basis.Side} Content for mod '{package.ModId}' is outside the accepted live composition.");
            packages.Add(package.ModId, package);
        }
        return packages;
    }

    private ModContentPackage AttachDependencies(ModContentPackage package, DiscoveredMod mod)
    {
        string? version = mod.Definition.MetaData.Version;
        if (_dependencies.TryGetValue(package, out var prior))
        {
            if (prior.Matches(version, mod.Dependencies)) return package;
            // The capture's existing claim covers this distinct candidate object over the same cooked store.
            package = package.WithNewIdentity();
        }
        _dependencies.Add(package, new PackageDependencies(version, mod.Dependencies));
        return package;
    }

    private ModContentPackage[] ResolveOrder(Dictionary<string, ModContentPackage> selected, CancellationToken cancellation)
    {
        // The frozen Lua entry-point flag remains authoritative, but startup's provisional cooked flag does not.
        var nodes = new List<ModDependencyNode>(_scripted.Count + selected.Count);
        foreach (string id in _scripted.Keys)
        {
            cancellation.ThrowIfCancellationRequested();
            nodes.Add(_startup.NodesById[id] with
            {
                CookedAvailability = selected.ContainsKey(id) ? ModCookedAvailability.AdmittedCandidate : ModCookedAvailability.Absent
            });
        }
        foreach (var (id, package) in selected)
        {
            cancellation.ThrowIfCancellationRequested();
            if (_scripted.ContainsKey(id)) continue;
            if (!_dependencies.TryGetValue(package, out var metadata))
                throw new InvalidOperationException($"Missing dependency metadata for Content package '{id}'.");
            nodes.Add(new ModDependencyNode(id, metadata.Version, metadata.Manifest, false, ModCookedAvailability.AdmittedCandidate));
        }
        var resolution = ModDependencyResolver.Resolve(nodes, _target, cancellation);
        ValidateFrozenLua(_startup, resolution, cancellation);
        return resolution.OrderedIds.Where(selected.ContainsKey).Select(id => selected[id]).ToArray();
    }

    private static void ValidateFrozenLua(ModStartupDependencies basis, ModDependencyResolution candidate, CancellationToken cancellation)
    {
        int index = 0;
        foreach (string id in candidate.OrderedIds)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!basis.PackagesById.TryGetValue(id, out var package) || package.Definition.MetaData.ContentOnly) continue;
            var before = basis.Resolution.DirectTargetsById[id];
            var after = candidate.DirectTargetsById[id];
            if (!before.SetEquals(after))
                throw new ModDependencyException($"Restart required on {basis.Side}: direct dependencies of active Lua mod '{id}' would change " +
                    $"from [{string.Join(", ", before.Order(StringComparer.Ordinal))}] to [{string.Join(", ", after.Order(StringComparer.Ordinal))}].");
            string expected = basis.ScriptedMods[index++].MetaData.Id;
            if (id != expected)
                throw new ModDependencyException($"Restart required on {basis.Side}: active Lua order would place '{id}' where '{expected}' was loaded.");
        }
    }

    private static string ModFolder(ModContentPackage package) => Path.GetDirectoryName(Path.GetDirectoryName(package.SourcePath))!;

    private sealed record PackageDependencies(string? Version, ModDependencyManifest Manifest)
    {
        public bool Matches(string? version, ModDependencyManifest manifest) => Version == version &&
            SameGroup(Manifest.Shared, manifest.Shared) && SameGroup(Manifest.Client, manifest.Client) &&
            SameGroup(Manifest.Server, manifest.Server);

        private static bool SameGroup(ImmutableArray<ModDependencyDeclaration> before, ImmutableArray<ModDependencyDeclaration> after) =>
            before.Length == after.Length && (before.AsSpan().SequenceEqual(after.AsSpan()) || before.ToHashSet().SetEquals(after));
    }
}
