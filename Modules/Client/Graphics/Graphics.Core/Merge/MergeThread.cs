using System.Drawing;
using System.Numerics;
using Karpik.Engine.Client.Graphics.Core.Presets;
using Karpik.Engine.Core;
using Karpik.Jobs;
using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public class MergeThread : IMergeThread, IOnInjectedDI
{
    public bool IsRunning => !_handle.IsCompleted;

    private CommandList _commandList;
    private JobHandle _handle;
    private readonly Lock _lock = new();
    
    [DI] private GraphicsDevice _device = null!;
    [DI] private Preset2DPipeline _2dPipeline = null!;
    
    private DeviceBuffer _quadVertexBuffer = null!;
    
    private readonly Vertex2D[] _rectVertices = new Vertex2D[4];
    
    public void OnInjected()
    {
        // Создаём буфер сразу после получения Device
        _quadVertexBuffer = QuadVertexBuffer.Create(_device);
        QuadVertexBuffer.Update(_device, _quadVertexBuffer);
        _commandList = _device.ResourceFactory.CreateCommandList();
    }

    public void BeginMerge()
    {
        var buffers = GraphicsContext.CollectBuffers();

        _handle = Job.Run(() =>
        {
            _commandList.Begin();
            _commandList.SetFramebuffer(_device.MainSwapchain.Framebuffer);
            _commandList.ClearColorTarget(0, Color.Green.VeldridFloat);
            
            foreach (var buffer in buffers)
            {
                foreach (var cmd in buffer.GetCommands())
                {
                    ExecuteCommand(_commandList, cmd);
                }
            }
            
            _commandList.End();
        });
    }

    public void WaitForCompletion() => _handle.Wait();

    public CommandList GetCommandList() => _commandList;

    private void ExecuteCommand(CommandList cmdList, DrawCommand cmd)
    {
        switch (cmd.Type)
        {
            case DrawCommandType.Rect:
                Vector4 color = new Vector4(
                    cmd.RectangleColor.R / 255f,
                    cmd.RectangleColor.G / 255f,
                    cmd.RectangleColor.B / 255f,
                    cmd.RectangleColor.A / 255f
                );
                float screenWidth = _device.MainSwapchain.Framebuffer.Width;
                float screenHeight = _device.MainSwapchain.Framebuffer.Height;
                float left   = (cmd.Rectangle.Left / screenWidth) * 2f - 1f;
                float right  = (cmd.Rectangle.Right / screenWidth) * 2f - 1f;
                float top    = 1f - (cmd.Rectangle.Top / screenHeight) * 2f;
                float bottom = 1f - (cmd.Rectangle.Bottom / screenHeight) * 2f;
                
                _rectVertices[0] = new Vertex2D { Position = new Vector2(left, top),    TexCoord = new Vector2(0, 0), Color = color };
                _rectVertices[1] = new Vertex2D { Position = new Vector2(right, top),   TexCoord = new Vector2(1, 0), Color = color };
                _rectVertices[2] = new Vertex2D { Position = new Vector2(left, bottom), TexCoord = new Vector2(0, 1), Color = color };
                _rectVertices[3] = new Vertex2D { Position = new Vector2(right, bottom),TexCoord = new Vector2(1, 1), Color = color };
                
                cmdList.UpdateBuffer(_quadVertexBuffer, 0, _rectVertices);
                
                cmdList.SetPipeline(_2dPipeline.RectPipeline);
                cmdList.SetVertexBuffer(0, _quadVertexBuffer);
                cmdList.SetGraphicsResourceSet(0, _2dPipeline.WhiteRectResourceSet);
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