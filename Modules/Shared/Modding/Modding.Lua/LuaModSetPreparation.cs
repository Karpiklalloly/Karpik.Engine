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
        AssetTarget target = ContentSide;
        string side = target.ToString();
        try
        {
            foreach (var mod in discovery.Candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!ContentCaptureFiles.DirectoryExists(Path.Combine(mod.Directory, "Content", side))) continue;
                    if (await ModContentCapture.CaptureAsync(mod.Directory, mod.MetaData.Id, target,
                            report: report, ct: cancellationToken).ConfigureAwait(false) is { } capture)
                        captures.Add(capture);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    report(new ContentDiagnostic("KCR-MOD-PACKAGE", ContentDiagnosticSeverity.Error, mod.Directory,
                        $"Rejected Content package for mod '{mod.MetaData.Id}': {error.Message}"));
                }
            }
            return (captures, discovery.InstalledOwnerIds);
        }
        catch (Exception error)
        {
            ModContentCapture.DisposeAll(captures, error);
            throw;
        }
    }

    public ModContentRefreshCapture? CreateRefreshCapture(Action<ContentDiagnostic> report)
    {
        if (registry is not ModsRegistry mods) return null;
        string modsDirectory = mods.ModsDirectoryPath;
        AssetTarget target = ContentSide;
        var scripted = mods.StartupDiscovery.ScriptedMods.ToDictionary(static mod => mod.MetaData.Id,
            static mod => mod.Directory, StringComparer.Ordinal);
        return (previous, cancellation) => LuaModContentRefresh.CaptureAsync(modsDirectory, target, scripted,
            previous, report, cancellation);
    }

    private AssetTarget ContentSide => application.ApplicationSide == Side.Client ? AssetTarget.Client : AssetTarget.Server;
}
