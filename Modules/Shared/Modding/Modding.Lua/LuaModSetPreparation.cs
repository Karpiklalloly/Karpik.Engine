using System.Collections.Frozen;
using System.Composition;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Modding.Lua;

[Export(typeof(IModSetPreparation))]
[Export(typeof(IModContentParticipant))]
[ServiceRegistration(ModuleScope.ModSet, ServiceLifetime.Singleton)]
public sealed class LuaModSetPreparation(IModsRegistry registry, Application application) : IModSetPreparation, IModContentParticipant
{
    private ModStartupDependencies? _startupCandidate;
    private LuaModContentRefresh? _refresh;

    public async ValueTask PrepareAsync(CancellationToken cancellationToken)
    {
        ExecutionSide side = application.ApplicationSide switch
        {
            Side.Client => ExecutionSide.Client,
            Side.Server => ExecutionSide.Server,
            _ => throw new InvalidOperationException("Unsupported application side.")
        };
        await registry.Scan(side, cancellationToken);
    }

    public async ValueTask<(IReadOnlyList<ModContentCapture> Captures, IReadOnlyCollection<string> InstalledModIds)> CaptureStartupAsync(
        Action<ContentDiagnostic> report, CancellationToken cancellationToken)
    {
        if (registry is not ModsRegistry mods) return ([], []);
        var discovery = mods.StartupDiscovery;
        var captures = new List<ModContentCapture>();
        var cooked = new Dictionary<string, ModCookedAvailability>(StringComparer.Ordinal);
        AssetTarget target = ContentSide;
        string side = target.ToCanonicalString();
        try
        {
            foreach (var (id, declared) in discovery.DeclaredMods)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (declared.UncapturableCookedSource is not { } source) continue;
                try
                {
                    if (ContentCaptureFiles.DirectoryExists(Path.Combine(source, "Content", side)))
                        cooked.Add(id, ModCookedAvailability.Rejected);
                }
                catch (Exception error) when (IsCookedRejection(error))
                {
                    cooked.Add(id, ModCookedAvailability.Rejected);
                }
            }

            foreach (var mod in discovery.Candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string id = mod.MetaData.Id;
                try
                {
                    if (!ContentCaptureFiles.DirectoryExists(Path.Combine(mod.Directory, "Content", side))) continue;
                    var capture = await ModContentCapture.CaptureAsync(mod.Directory, id, target,
                        report: report, ct: cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidDataException($"{side} Content disappeared during capture.");
                    captures.Add(capture);
                    cooked.Add(id, ModCookedAvailability.AdmittedCandidate);
                }
                catch (Exception error) when (IsCookedRejection(error))
                {
                    cooked.Add(id, ModCookedAvailability.Rejected);
                    report(new ContentDiagnostic("KCR-MOD-PACKAGE", ContentDiagnosticSeverity.Error, mod.Directory,
                        $"Rejected Content package for mod '{id}': {error.Message}"));
                }
            }

            var startup = mods.ResolveStartupDependencies(target, cooked, cancellationToken);
            var capturesById = captures.ToDictionary(static capture => capture.Package.ModId, StringComparer.Ordinal);
            var orderedCaptures = startup.Resolution.OrderedIds.Where(capturesById.ContainsKey)
                .Select(id => capturesById[id]).ToArray();
            _startupCandidate = startup;
            return (orderedCaptures, discovery.InstalledOwnerIds);
        }
        catch (Exception error)
        {
            ModContentCapture.DisposeAll(captures, error);
            throw;
        }
    }

    public void CompleteStartupWithoutContent(CancellationToken cancellationToken)
    {
        if (registry is ModsRegistry mods)
            mods.PublishStartupDependencies(mods.ResolveStartupDependencies(ContentSide,
                FrozenDictionary<string, ModCookedAvailability>.Empty, cancellationToken));
    }

    public void ValidateStartupPackages(IReadOnlyList<ModContentPackage> packages, CancellationToken cancellationToken)
    {
        if (registry is not ModsRegistry mods) return;
        var startup = _startupCandidate ?? throw new InvalidOperationException("Startup Content was not captured.");
        _startupCandidate = null;
        var participating = startup.Resolution.ParticipatingCookedOnlyProviderIds;
        var accepted = packages.Select(static package => package.ModId).ToHashSet(StringComparer.Ordinal);
        if (participating.Count != 0)
        {
            foreach (string id in startup.Resolution.OrderedIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!participating.Contains(id) || accepted.Contains(id)) continue;
                var related = startup.Resolution.DirectTargetsById[id].Concat(startup.Resolution.DirectTargetsById
                    .Where(entry => entry.Value.Contains(id)).Select(static entry => entry.Key)).Order(StringComparer.Ordinal);
                throw new ModDependencyException($"Mod '{id}' on {ContentSide}: Content composition rejected its participating cooked package and no Lua payload is available. Related dependencies: {string.Join(", ", related)}.");
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        startup = startup.AcceptCooked(accepted);
        _refresh = new LuaModContentRefresh(mods.ModsDirectoryPath, ContentSide, startup, packages, mods.StartupDiscovery.DisabledIds);
        mods.PublishStartupDependencies(startup);
    }

    private static bool IsCookedRejection(Exception error) => error is IOException or UnauthorizedAccessException or InvalidDataException;

    public ModContentRefreshCapture? CreateRefreshCapture(Action<ContentDiagnostic> report)
    {
        return _refresh is { } refresh
            ? (previous, cancellation) => refresh.CaptureAsync(previous, report, cancellation)
            : null;
    }

    // The opt-in host supplies the accepted Simulation view; it is read only on the serialized cold worker.
    internal ModContentRefreshCapture? CreateLiveRefreshCapture(Func<ModStartupDependencies> getBasis, Action<ContentDiagnostic> report)
    {
        return _refresh is not null && LuaSourceCapture.IsSupported
            ? (previous, cancellation) => LuaModContentRefresh.CaptureLiveAsync(getBasis(), previous, report, cancellation)
            : null;
    }

    // Null means Busy. A non-null result belongs to the host: commit at its barrier or dispose to abort,
    // then join Update.Completion on the cold path (not while holding a read or renderer claim).
    internal async Task<(ModStartupDependencies Composition, ContentReloadCoordinator.PreparedModUpdate Update)?> PrepareLiveSelectionAsync(
        ContentReloadCoordinator coordinator, ModStartupDependencies basis, IReadOnlyCollection<string> selectedIds,
        Action<ContentDiagnostic> report, CancellationToken cancellationToken = default)
    {
        if (registry is not ModsRegistry mods || _refresh is null || basis.Side != ContentSide)
            throw new InvalidOperationException("Live mod selection requires stock ModSet Content startup on the same side.");
        if (selectedIds.Count > LuaSourceCapture.MaximumPackages)
            throw new InvalidDataException($"Selected mod set exceeds {LuaSourceCapture.MaximumPackages} packages.");
        var ids = selectedIds.ToArray();
        ModStartupDependencies? composition = null;
        var update = await coordinator.PrepareModUpdateAsync(CaptureSelectionAsync, report, cancellationToken).ConfigureAwait(false);
        return update is null ? null : (composition!, update);

        async Task<(IReadOnlyList<ModContentPackage>, IReadOnlyCollection<string>, IReadOnlyList<ModContentCapture>)> CaptureSelectionAsync(
            IReadOnlyList<ModContentPackage> previous, CancellationToken cancellation)
        {
            var packages = LuaModContentRefresh.IndexAcceptedPackages(basis, previous);
            // Refresh can change cooked availability without changing the accepted Lua composition.
            var selection = mods.DiscoverSelection(basis.AcceptCooked(packages.Keys.ToHashSet(StringComparer.Ordinal)), ids, cancellation);
            var captures = new List<ModContentCapture>();
            try
            {
                foreach (string id in selection.NewIds.Order(StringComparer.Ordinal))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var discovered = selection.PackagesById[id];
                    if (discovered.UncapturableCookedSource is not null) continue;
                    string path = discovered.Definition.Directory;
                    var capture = await ModContentCapture.CaptureAsync(path, id, basis.Side, report: report, ct: cancellation).ConfigureAwait(false);
                    if (capture is not null)
                    {
                        captures.Add(capture);
                        packages.Add(id, capture.Package);
                    }
                    if (!ContentCaptureFiles.DirectoryExists(path))
                        throw new InvalidDataException($"Selected mod '{id}' disappeared during Content capture: '{path}'.");
                }

                composition = ModsRegistry.ComposeSelection(selection, captures.Select(static capture => capture.Package.ModId).ToArray(), cancellation);
                return (composition.Resolution.OrderedIds.Where(packages.ContainsKey).Select(id => packages[id]).ToArray(),
                    selection.PackagesById.Keys, captures);
            }
            catch (Exception error)
            {
                ModContentCapture.DisposeAll(captures, error);
                throw;
            }
        }
    }

    private AssetTarget ContentSide => application.ApplicationSide == Side.Client ? AssetTarget.Client : AssetTarget.Server;
}
