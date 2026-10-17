namespace Karpik.Engine.Core;

/// <summary>
/// Runs on the client platform thread immediately before a simulation frame starts.
/// </summary>
public interface ISystemMainThreadFrameBegin : ISystem
{
    void MainThreadFrameBegin();
}
