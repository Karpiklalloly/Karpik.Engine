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
            var cl = context.CommandList;
            cl.Begin();
            cl.SetFramebuffer(_device.MainSwapchain.Framebuffer);
            cl.ClearColorTarget(0, Color.Green.VeldridFloat);

            cl.SetPipeline(_2dPipeline.RectPipeline);
            cl.SetVertexBuffer(0, context.VertexBuffer);
            cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt32);
            
            _currentQuadCount = 0;
            ResourceSet? currentRS = null;
            Pipeline? currentPipeline = _2dPipeline.RectPipeline;
            
            foreach (var buffer in buffers)
            {
                // 1. Сначала рисуем все прямоугольники (они всегда батчатся вместе)
                var rects = buffer.GetRectCommands();
                if (rects.Length > 0)
                {
                    SetPipeline(ref currentPipeline, _2dPipeline.RectPipeline, context, currentRS);
                    SetTexture(ref currentRS, _2dPipeline.WhiteRectResourceSet, context);
                    foreach (ref readonly var cmd in rects)
                    {
                        AddRectToBatch(in cmd, context, sw, sh);
                        if (_currentQuadCount >= MaxQuads) Flush(context, currentRS);
                    }
                }
                
                // 2. Затем рисуем текстуры
                var textures = buffer.GetTextureCommands();
                if (textures.Length > 0)
                {
                    SetPipeline(ref currentPipeline, _2dPipeline.TexturePipeline, context, currentRS);
                    foreach (ref readonly var cmd in textures)
                    {
                        var vTex = (VeldridTexture2D)cmd.Texture;
                        // Если текстура сменилась — рисуем то, что накопили
                        SetTexture(ref currentRS, vTex.ResourceSet, context);

                        AddTextureToBatch(in cmd, context, sw, sh);
                        if (_currentQuadCount >= MaxQuads) Flush(context, currentRS);
                    }
                }
            }

            if (_currentQuadCount > 0)
            {
                Flush(context, currentRS);
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

    private void AddTextureToBatch(in DrawTextureCmd cmd, in MergeContext context, float sw, float sh)
    {
        Vector4 color = new Vector4(
            cmd.Color.R / 255f,
            cmd.Color.G / 255f,
            cmd.Color.B / 255f,
            cmd.Color.A / 255f
        );

        float l = (cmd.Position.X / sw) * 2f - 1f;
        float r = ((cmd.Position.X + cmd.Size.X) / sw) * 2f - 1f;
        float t = 1f - (cmd.Position.Y / sh) * 2f;
        float b = 1f - ((cmd.Position.Y + cmd.Size.Y) / sh) * 2f;

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

    private void SetPipeline(ref Pipeline? current, Pipeline next, MergeContext ctx, ResourceSet? currentRS)
    {
        if (current == next) return;

        Flush(ctx, currentRS);
        current = next;
        ctx.CommandList.SetPipeline(next);
    }
    
    private void SetTexture(ref ResourceSet? current, ResourceSet next, MergeContext ctx)
    {
        if (current != next)
        {
            Flush(ctx, current);
            current = next;
            ctx.CommandList.SetGraphicsResourceSet(0, next);
        }
    }
    
    private void Flush(MergeContext context, ResourceSet? rs)
    {
        if (_currentQuadCount == 0 || rs == null) return;

        uint sizeInBytes = (uint)(_currentQuadCount * 4 * Vertex2D.SizeInBytes);
        context.CommandList.UpdateBuffer(context.VertexBuffer, 0, ref context.Vertices[0], sizeInBytes);
        context.CommandList.DrawIndexed((uint)(_currentQuadCount * 6));

        _currentQuadCount = 0;
    }
}
