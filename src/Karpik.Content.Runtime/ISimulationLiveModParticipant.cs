using Karpik.Content.Core;
using Karpik.Engine.Core;

namespace Karpik.Content.Runtime;

/// <summary>Module bridge for coordinated live selection, not a host admission API.</summary>
public interface ISimulationLiveModParticipant : ISimulationCodeReloadParticipant
{
    /// <summary>Read only the accepted Simulation composition when the refresh worker invokes the callback.</summary>
    ModContentRefreshCapture? CreateLiveModRefreshCapture(Action<ContentDiagnostic> report);

    /// <summary>Stage both roots without publishing. Null means the Content coordinator is busy.</summary>
    ValueTask<ISimulationCodeReloadCandidate?> PrepareLiveModAsync(ContentReloadCoordinator? coordinator,
        IReadOnlyCollection<string> selectedIds, Action<ContentDiagnostic> report, CancellationToken cancellationToken);
}
