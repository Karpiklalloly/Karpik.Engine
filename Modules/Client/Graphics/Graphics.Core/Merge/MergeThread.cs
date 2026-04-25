using System.Drawing;
using System.Numerics;
using Karpik.Engine.Client.Graphics.Core.Presets;
using Karpik.Engine.Core;
using Karpik.Jobs;
using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public class MergeThread : IMergeThread, IOnInjectedDI
{
    private const int MaxQuads = 16000;
    private const int MaxVertices = MaxQuads * 4;
    private const int MaxIndices = MaxQuads * 6;
    
    public bool IsRunning => !_handle.IsCompleted;

    private JobHandle _handle;
    
    [DI] private GraphicsDevice _device = null!;
    [DI] private Preset2DPipeline _2dPipeline = null!;
    
    // TODO: Сделать возможность переключиться на ushort
    private DeviceBuffer _indexBuffer = null!;
    private int _currentQuadCount = 0;

    private MergeContext _mergeContextA;
    private MergeContext _mergeContextB;
    
    private bool _isUsingA = true;
    private MergeContext _currentContext => _isUsingA ? _mergeContextA : _mergeContextB;
    private MergeContext _submittedContext => _isUsingA ? _mergeContextB : _mergeContextA; // Тот, что был заполнен в прошлый раз
    
    public void OnInjected()
    {
        var factory = _device.ResourceFactory;
        uint[] indices = new uint[MaxIndices];
        for (int i = 0; i < MaxQuads; i++)
        {
            int v = i * 4;
            int id = i * 6;
            indices[id + 0] = (uint)(v + 0);
            indices[id + 1] = (uint)(v + 1);
            indices[id + 2] = (uint)(v + 2);
            indices[id + 3] = (uint)(v + 2);
            indices[id + 4] = (uint)(v + 1);
            indices[id + 5] = (uint)(v + 3);
        }
        
        _indexBuffer = factory.CreateBuffer(new BufferDescription(
            MaxIndices * sizeof(uint), BufferUsage.IndexBuffer));
        _device.UpdateBuffer(_indexBuffer, 0, indices);

        _mergeContextA = new MergeContext()
        {
            VertexBuffer = factory.CreateBuffer(new BufferDescription(
                MaxVertices * Vertex2D.SizeInBytes, BufferUsage.VertexBuffer | BufferUsage.Dynamic)),
            Vertices = new Vertex2D[MaxVertices],
            CommandList = factory.CreateCommandList()
        };
        
        _mergeContextB = new MergeContext()
        {
            VertexBuffer = factory.CreateBuffer(new BufferDescription(
                MaxVertices * Vertex2D.SizeInBytes, BufferUsage.VertexBuffer | BufferUsage.Dynamic)),
            Vertices = new Vertex2D[MaxVertices],
            CommandList = factory.CreateCommandList()
        };
    }

    public void BeginMerge()
    {
        var buffers = GraphicsContext.CollectBuffers();
        _isUsingA = !_isUsingA; // Переключаем буферы
        
        float sw = _device.MainSwapchain.Framebuffer.Width;
        float sh = _device.MainSwapchain.Framebuffer.Height;

        _handle = Job.Run(() =>
        {
            var context = _currentContext;
            context.CommandList.Begin();
            
            _currentQuadCount = 0;
            
            foreach (var buffer in buffers)
            {
                foreach (ref readonly var cmd in buffer.GetRectCommands())
                {
                    AddRectToBatch(in cmd, in context, sw, sh);

                    if (_currentQuadCount >= MaxQuads)
                    {
                        break;
                    }
                }
            }

            if (_currentQuadCount > 0)
            {
                uint sizeInBytes = (uint)(_currentQuadCount * 4 * Vertex2D.SizeInBytes);
                context.CommandList.UpdateBuffer(context.VertexBuffer, 0, ref context.Vertices[0], sizeInBytes);
                
                
                context.CommandList.SetFramebuffer(_device.MainSwapchain.Framebuffer);
                context.CommandList.ClearColorTarget(0, Color.Green.VeldridFloat);

                context.CommandList.SetPipeline(_2dPipeline.RectPipeline);
                context.CommandList.SetGraphicsResourceSet(0, _2dPipeline.WhiteRectResourceSet);
                context.CommandList.SetVertexBuffer(0, context.VertexBuffer);
                context.CommandList.SetIndexBuffer(_indexBuffer, IndexFormat.UInt32);
            
                context.CommandList.DrawIndexed((uint)(_currentQuadCount * 6));
            }
            else
            {
                context.CommandList.SetFramebuffer(_device.MainSwapchain.Framebuffer);
                context.CommandList.ClearColorTarget(0, Color.Green.VeldridFloat);
            }
            
            context.CommandList.End();
        });
    }

    public void WaitForCompletion() => _handle.Wait();

    public CommandList GetCommandList() => _submittedContext.CommandList;
    
    private void AddRectToBatch(in DrawRectCmd cmd, in MergeContext context, float sw, float sh)
    {
        Vector4 color = new Vector4(
            cmd.Color.R / 255f,
            cmd.Color.G / 255f,
            cmd.Color.B / 255f,
            cmd.Color.A / 255f
        );

        float l = (cmd.Rectangle.Left / sw) * 2f - 1f;
        float r = (cmd.Rectangle.Right / sw) * 2f - 1f;
        float t = 1f - (cmd.Rectangle.Top / sh) * 2f;
        float b = 1f - (cmd.Rectangle.Bottom / sh) * 2f;

        int offset = _currentQuadCount * 4;
        context.Vertices[offset + 0] = new Vertex2D
        { 
            Position = new Vector2(l, t),
            TexCoord = new Vector2(0, 0), Color = color
        };
        context.Vertices[offset + 1] = new Vertex2D
        {
            Position = new Vector2(r, t),
            TexCoord = new Vector2(1, 0), Color = color
        };
        context.Vertices[offset + 2] = new Vertex2D
        {
            Position = new Vector2(l, b),
            TexCoord = new Vector2(0, 1), Color = color
        };
        context.Vertices[offset + 3] = new Vertex2D
        {
            Position = new Vector2(r, b),
            TexCoord = new Vector2(1, 1), Color = color
        };

        _currentQuadCount++;
    }
}