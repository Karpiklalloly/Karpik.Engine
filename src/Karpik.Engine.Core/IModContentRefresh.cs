namespace Karpik.Engine.Core;

/// <summary>Requests a local mod Content update without reloading the simulation or scripts.</summary>
public interface IModContentRefresh
{
    /// <summary>Queue an update for the next quiescent frame boundary. Await it without blocking the frame loop.</summary>
    Task<ModContentRefreshResult> RequestModContentRefreshAsync();
}
