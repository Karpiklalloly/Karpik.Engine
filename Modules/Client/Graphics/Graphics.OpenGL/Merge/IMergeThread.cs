using Veldrid;

namespace Karpik.Engine.Client.Graphics.OpenGL;

public interface IMergeThread : IDisposable
{
    public bool IsRunning { get; }

    public bool TryBeginMerge();

    public void BeginMerge();

    public void WaitForCompletion();

    public bool TryGetCompletedCommandList(out CommandList? commandList, out Fence? submitFence);

    public CommandList GetCommandList();
}
