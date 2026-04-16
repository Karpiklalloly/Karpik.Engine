using Karpik.Jobs;
using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public class MergeThread(GraphicsDevice device, JobSystem jobSystem) : IMergeThread
{
    public bool IsRunning => !_handle.IsCompleted;

    private CommandList? _commandList;
    private JobHandle _handle;
    private readonly Lock _lock = new();

    public void BeginMerge()
    {
        var buffers = GraphicsContext.CollectBuffers();

        _handle = jobSystem.Enqueue(() =>
        {
            var cmdList = device.ResourceFactory.CreateCommandList();
            cmdList.Begin();
            
            foreach (var buffer in buffers)
            {
                foreach (var cmd in buffer.GetCommands())
                {
                    ExecuteCommand(cmdList, cmd);
                }
            }
            
            cmdList.End();
            _commandList = cmdList;
        }, Span<JobHandle>.Empty);
    }

    public void WaitForCompletion()
    {
        _handle.Wait();
    }

    public CommandList? GetCommandList()
    {
        return _commandList;
    }
    
    private void ExecuteCommand(CommandList cmdList, DrawCommand cmd)
    {
        switch (cmd.Type)
        {
            case DrawCommandType.Rect:
                // cmdList.SetPipeline(...);
                // cmdList.SetVertexBuffer(...);
                // cmdList.Draw(...);
                break;
            
            case DrawCommandType.Texture:
                // ...
                break;
            
            case DrawCommandType.Text:
                // ...
                break;
            case DrawCommandType.None:
            default:
                return;
        }
    }
}