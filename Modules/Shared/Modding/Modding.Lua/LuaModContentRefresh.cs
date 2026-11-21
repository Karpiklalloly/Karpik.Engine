using Karpik.Content.Core;
using Karpik.Content.Runtime;

namespace Karpik.Engine.Shared.Modding.Lua;

internal static class LuaModContentRefresh
{
    public static async Task<(IReadOnlyList<ModContentPackage>, IReadOnlyCollection<string>, IReadOnlyList<ModContentCapture>)> CaptureAsync(
        string modsDirectory, AssetTarget target, IReadOnlyDictionary<string, string> scripted,
        IReadOnlyList<ModContentPackage> previous, Action<ContentDiagnostic> report, CancellationToken cancellation)
    {
        ModDiscoveryResult discovery = ModsRegistry.DiscoverAt(modsDirectory, target, cancellation);
        if (discovery.Diagnostics.Count != 0)
            throw new InvalidDataException(string.Join(Environment.NewLine, discovery.Diagnostics));

        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var activeScriptPaths = scripted.Values.ToHashSet(pathComparer);
        var currentPaths = discovery.Candidates.Select(static mod => mod.Directory).ToHashSet(pathComparer);
        var owners = discovery.InstalledOwnerIds.ToHashSet(StringComparer.Ordinal);
        owners.UnionWith(scripted.Keys);
        var previousById = previous.ToDictionary(static package => package.ModId, StringComparer.Ordinal);
        var selected = new SortedDictionary<string, ModContentPackage>(StringComparer.Ordinal);
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
                    selected.Add(id, capture.Package);
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
            cancellation.ThrowIfCancellationRequested();
            return (selected.Values.ToArray(), owners, captures);
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

    private static string ModFolder(ModContentPackage package) => Path.GetDirectoryName(Path.GetDirectoryName(package.SourcePath))!;
}
