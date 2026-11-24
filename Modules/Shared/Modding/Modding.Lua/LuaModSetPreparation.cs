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
        if (participating.Count != 0)
        {
            var accepted = packages.Select(static package => package.ModId).ToHashSet(StringComparer.Ordinal);
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
        _refresh = new LuaModContentRefresh(mods.ModsDirectoryPath, ContentSide, startup, packages);
        mods.PublishStartupDependencies(startup);
    }

    private static bool IsCookedRejection(Exception error) => error is IOException or UnauthorizedAccessException or InvalidDataException;

    public ModContentRefreshCapture? CreateRefreshCapture(Action<ContentDiagnostic> report)
    {
        return _refresh is { } refresh
            ? (previous, cancellation) => refresh.CaptureAsync(previous, report, cancellation)
            : null;
    }

    private AssetTarget ContentSide => application.ApplicationSide == Side.Client ? AssetTarget.Client : AssetTarget.Server;
}
