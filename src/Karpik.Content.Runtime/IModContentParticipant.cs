using Karpik.Content.Core;

namespace Karpik.Content.Runtime;

/// <summary>Optional ModSet contribution after its startup discovery has completed.</summary>
public interface IModContentParticipant
{
    /// <summary>The host owns the returned capture claims until it finalizes the startup catalog.</summary>
    ValueTask<(IReadOnlyList<ModContentCapture> Captures, IReadOnlyCollection<string> InstalledModIds)> CaptureStartupAsync(
        Action<ContentDiagnostic> report, CancellationToken cancellationToken);

    /// <summary>Freeze the startup inventory. The returned callback must not depend on disposable ModSet services.</summary>
    ModContentRefreshCapture? CreateRefreshCapture(Action<ContentDiagnostic> report);
}

/// <summary>Borrow previous packages only for the call; ownership of captures transfers on successful return.</summary>
public delegate Task<(IReadOnlyList<ModContentPackage> Packages, IReadOnlyCollection<string> InstalledModIds,
    IReadOnlyList<ModContentCapture> Captures)> ModContentRefreshCapture(IReadOnlyList<ModContentPackage> previous,
    CancellationToken cancellationToken);
