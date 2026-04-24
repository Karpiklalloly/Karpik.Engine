using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public interface IMergeThread
{
    public bool IsRunning { get; }
    
    public void BeginMerge();
    
    public void WaitForCompletion();
    
    public CommandList GetCommandList();
}