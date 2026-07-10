using Veldrid;

namespace Karpik.Engine.Client.Graphics.OpenGL;

public interface IMergeThread : IDisposable
{
    public bool IsRunning { get; }

    public bool TryBeginMerge();

    public void BeginMerge();

    public void WaitForCompletion();

    /// <summary>
    /// Atomically transfers ownership of the latest completed command list to the caller.
    /// A command list may be taken and submitted only once before it is rebuilt.
    /// </summary>
    public bool TryTakeCompletedCommandList(out CommandList? commandList, out Fence? submitFence);

    public CommandList GetCommandList();
}
