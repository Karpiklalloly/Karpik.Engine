using System.Drawing;
using System.Numerics;
using Karpik.Engine.Client.Graphics.Core.Presets;
using Karpik.Engine.Core;
using Karpik.Jobs;
using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public class MergeThread : IMergeThread, IOnInjectedDI
{
    private const int MaxQuads = 1000;
    private const int MaxVertices = MaxQuads * 4;
    private const int MaxIndices = MaxQuads * 6;
    
    public bool IsRunning => !_handle.IsCompleted;

    private CommandList _commandList;
    private JobHandle _handle;
    
    [DI] private GraphicsDevice _device = null!;
    [DI] private Preset2DPipeline _2dPipeline = null!;
    
    private DeviceBuffer _vertexBuffer = null!;
    private DeviceBuffer _indexBuffer = null!;
    
    private readonly Vertex2D[] _vertices = new Vertex2D[MaxVertices];
    private int _currentQuadCount = 0;
    
    public void OnInjected()
    {
        var factory = _device.ResourceFactory;
        _vertexBuffer = factory.CreateBuffer(new BufferDescription(
            MaxVertices * Vertex2D.SizeInBytes, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        
        ushort[] indices = new ushort[MaxIndices];
        for (int i = 0; i < MaxQuads; i++)
        {
            int v = i * 4;
            int id = i * 6;
            indices[id + 0] = (ushort)(v + 0);
            indices[id + 1] = (ushort)(v + 1);
            indices[id + 2] = (ushort)(v + 2);
            indices[id + 3] = (ushort)(v + 2);
            indices[id + 4] = (ushort)(v + 1);
            indices[id + 5] = (ushort)(v + 3);
        }
        
        _indexBuffer = factory.CreateBuffer(new BufferDescription(
            MaxIndices * sizeof(ushort), BufferUsage.IndexBuffer));
        _device.UpdateBuffer(_indexBuffer, 0, indices);

        _commandList = factory.CreateCommandList();
    }

    public void BeginMerge()
    {
        var buffers = GraphicsContext.CollectBuffers();

        _handle = Job.Run(() =>
        {
            _commandList.Begin();
            _commandList.SetFramebuffer(_device.MainSwapchain.Framebuffer);
            _commandList.ClearColorTarget(0, Color.Green.VeldridFloat);
            
            _commandList.SetPipeline(_2dPipeline.RectPipeline);
            _commandList.SetGraphicsResourceSet(0, _2dPipeline.WhiteRectResourceSet);
            _commandList.SetVertexBuffer(0, _vertexBuffer);
            _commandList.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
            
            _currentQuadCount = 0;
            
            foreach (var buffer in buffers)
            {
                foreach (ref readonly var cmd in buffer.GetRectCommands())
                {
                    AddRectToBatch(in cmd);

                    if (_currentQuadCount >= MaxQuads)
                    {
                        Flush();
                    }
                }
            }
            
            Flush(); // Отрисовываем остаток
            _commandList.End();
        });
    }

    public void WaitForCompletion() => _handle.Wait();

    public CommandList GetCommandList() => _commandList;
    
    private void AddRectToBatch(in DrawRectCmd cmd)
    {
        Vector4 color = new Vector4(
            cmd.Color.R / 255f,
            cmd.Color.G / 255f,
            cmd.Color.B / 255f,
            cmd.Color.A / 255f
        );

        float sw = _device.MainSwapchain.Framebuffer.Width;
        float sh = _device.MainSwapchain.Framebuffer.Height;

        float l = (cmd.Rectangle.Left / sw) * 2f - 1f;
        float r = (cmd.Rectangle.Right / sw) * 2f - 1f;
        float t = 1f - (cmd.Rectangle.Top / sh) * 2f;
        float b = 1f - (cmd.Rectangle.Bottom / sh) * 2f;

        int offset = _currentQuadCount * 4;
        _vertices[offset + 0] = new Vertex2D
        { 
            Position = new Vector2(l, t),
            TexCoord = new Vector2(0, 0), Color = color
        };
        _vertices[offset + 1] = new Vertex2D
        {
            Position = new Vector2(r, t),
            TexCoord = new Vector2(1, 0), Color = color
        };
        _vertices[offset + 2] = new Vertex2D
        {
            Position = new Vector2(l, b),
            TexCoord = new Vector2(0, 1), Color = color
        };
        _vertices[offset + 3] = new Vertex2D
        {
            Position = new Vector2(r, b),
            TexCoord = new Vector2(1, 1), Color = color
        };

        _currentQuadCount++;
    }
    
    private void Flush()
    {
        if (_currentQuadCount == 0) return;

        // Обновляем только ту часть буфера, которую заполнили
        _commandList.UpdateBuffer(_vertexBuffer, 0, _vertices);
        _commandList.DrawIndexed((uint)(_currentQuadCount * 6));

        _currentQuadCount = 0;
    }
}