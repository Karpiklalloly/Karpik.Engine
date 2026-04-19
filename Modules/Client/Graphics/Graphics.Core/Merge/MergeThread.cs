using Karpik.Engine.Client.Graphics.Core.Presets;
using Karpik.Engine.Core;
using Karpik.Jobs;
using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public class MergeThread : IMergeThread, IOnInjectedDI
{
    public bool IsRunning => !_handle.IsCompleted;

    private CommandList? _commandList;
    private JobHandle _handle;
    private readonly Lock _lock = new();
    
    [DI] private GraphicsDevice _device = null!;
    [DI] private Preset2DPipeline _2dPipeline = null!;
    
    private DeviceBuffer _quadVertexBuffer = null!;
    
    public void OnInjected()
    {
        // Создаём буфер сразу после получения Device
        _quadVertexBuffer = QuadVertexBuffer.Create(_device);
        QuadVertexBuffer.Update(_device, _quadVertexBuffer);
    }

    public void BeginMerge()
    {
        var buffers = GraphicsContext.CollectBuffers();

        _handle = Job.Run(() =>
        {
            var cmdList = _device.ResourceFactory.CreateCommandList();
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
        });
    }

    public void WaitForCompletion() => _handle.Wait();

    public CommandList? GetCommandList() => _commandList;

    private void ExecuteCommand(CommandList cmdList, DrawCommand cmd)
    {
        switch (cmd.Type)
        {
            case DrawCommandType.Rect:
                cmdList.SetPipeline(_2dPipeline.RectPipeline);
                cmdList.SetVertexBuffer(0, _quadVertexBuffer);
                cmdList.Draw(4);
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