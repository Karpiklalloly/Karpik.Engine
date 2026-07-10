namespace Karpik.Engine.Core;

/// <summary>
/// Runs on the client platform thread before a simulation frame is requested.
/// </summary>
public interface ISystemMainThreadBegin : ISystem
{
    void MainThreadBegin();
}
