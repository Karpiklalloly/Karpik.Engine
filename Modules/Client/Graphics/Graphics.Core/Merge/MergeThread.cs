using System.Drawing;
using System.Numerics;
using System.Runtime.ExceptionServices;
using Karpik.Engine.Client.Graphics.Core.Presets;
using Karpik.Engine.Core;
using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public class MergeThread : IMergeThread, IOnInjectedDI
{
    private const int MaxQuads = 16000;
    private const int MaxVertices = MaxQuads * 4;
    private const int MaxIndices = MaxQuads * 6;

    public bool IsRunning => !_completed.IsSet;

    private readonly AutoResetEvent _workAvailable = new(false);
    private readonly ManualResetEventSlim _completed = new(true);
    private readonly Thread _workerThread;
    private volatile bool _shutdown;
    private Exception? _workerException;

    [DI] private GraphicsDevice _device = null!;
    [DI] private Preset2DPipeline _2dPipeline = null!;
    [DI] private GraphicsCameraState _cameraState = null!;

    // TODO: Сделать возможность переключиться на ushort
    private DeviceBuffer _indexBuffer = null!;

    private MergeContext _mergeContextA;
    private MergeContext _mergeContextB;
    private MergeContext _buildContext;
    private List<ICommandBuffer> _buildBuffers = null!;
    private Framebuffer _buildFramebuffer = null!;
    private Camera2D _buildCamera;
    private float _buildFramebufferWidth;
    private float _buildFramebufferHeight;

    private bool _isUsingA = true;
    private MergeContext _currentContext => _isUsingA ? _mergeContextA : _mergeContextB;

    public MergeThread()
    {
        _workerThread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
            Name = "GraphicsMerge"
        };
        _workerThread.Start();
    }

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
            TextGlyphs = new TextGlyphQuad[MaxQuads],
            CommandList = factory.CreateCommandList()
        };

        _mergeContextB = new MergeContext()
        {
            VertexBuffer = factory.CreateBuffer(new BufferDescription(
                MaxVertices * Vertex2D.SizeInBytes, BufferUsage.VertexBuffer | BufferUsage.Dynamic)),
            Vertices = new Vertex2D[MaxVertices],
            TextGlyphs = new TextGlyphQuad[MaxQuads],
            CommandList = factory.CreateCommandList()
        };
    }

    public void BeginMerge()
    {
        if (!_completed.IsSet)
        {
            WaitForCompletion();
        }

        var buffers = GraphicsContext.CollectBuffers();
        _isUsingA = !_isUsingA; // Переключаем буферы
        _buildContext = _currentContext;
        _buildBuffers = buffers;
        _buildFramebuffer = _device.MainSwapchain.Framebuffer;
        _buildFramebufferWidth = _buildFramebuffer.Width;
        _buildFramebufferHeight = _buildFramebuffer.Height;
        _cameraState.CaptureForFrame(_buildFramebufferWidth, _buildFramebufferHeight);
        _buildCamera = _cameraState.FrameCamera;
        _workerException = null;
        _completed.Reset();
        _workAvailable.Set();
    }

    public void WaitForCompletion()
    {
        _completed.Wait();
        if (_workerException != null)
        {
            ExceptionDispatchInfo.Capture(_workerException).Throw();
        }
    }

    public CommandList GetCommandList() => _currentContext.CommandList;

    public void Dispose()
    {
        _shutdown = true;
        _workAvailable.Set();
        _workerThread.Join();
        _workAvailable.Dispose();
        _completed.Dispose();
    }

    private void WorkerLoop()
    {
        while (true)
        {
            _workAvailable.WaitOne();
            if (_shutdown)
            {
                return;
            }

            try
            {
                BuildCommandList();
            }
            catch (Exception ex)
            {
                _workerException = ex;
            }
            finally
            {
                _completed.Set();
            }
        }
    }

    private void BuildCommandList()
    {
        var context = _buildContext;

        float sw = _buildFramebufferWidth;
        float sh = _buildFramebufferHeight;
        Camera2D camera = _buildCamera;

        var cl = context.CommandList;
        cl.Begin();
        cl.SetFramebuffer(_buildFramebuffer);
        cl.ClearColorTarget(0, Color.Green.VeldridFloat);

        cl.SetVertexBuffer(0, context.VertexBuffer);
        cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt32);

        int quadCount = 0;
        ResourceSet? currentRS = null;
        Pipeline? currentPipeline = null;

        foreach (var buffer in _buildBuffers)
        {
            var rects = buffer.GetRectCommands();
            var textures = buffer.GetTextureCommands();
            var texts = buffer.GetTextCommands();
            var commands = ((IOrderedCommandBuffer)buffer).GetCommands();

            foreach (ref readonly var command in commands)
            {
                switch (command.Type)
                {
                    case DrawCommandType.Rect:
                    {
                        SetPipeline(ref currentPipeline, _2dPipeline.RectPipeline, context, currentRS, ref quadCount);
                        SetTexture(ref currentRS, _2dPipeline.WhiteRectResourceSet, context, ref quadCount);

                        ref readonly var cmd = ref rects[command.Index];
                        AddRectToBatch(in cmd, context, in camera, sw, sh, ref quadCount);
                        if (quadCount >= MaxQuads) Flush(context, currentRS, ref quadCount);
                        break;
                    }
                    case DrawCommandType.Texture:
                    {
                        SetPipeline(ref currentPipeline, _2dPipeline.TexturePipeline, context, currentRS, ref quadCount);

                        ref readonly var cmd = ref textures[command.Index];
                        var vTex = (VeldridTexture2D)cmd.Texture;
                        SetTexture(ref currentRS, vTex.ResourceSet, context, ref quadCount);

                        AddTextureToBatch(in cmd, context, in camera, sw, sh, ref quadCount);
                        if (quadCount >= MaxQuads) Flush(context, currentRS, ref quadCount);
                        break;
                    }
                    case DrawCommandType.Text:
                    {
                        SetPipeline(ref currentPipeline, _2dPipeline.TextPipeline, context, currentRS, ref quadCount);

                        ref readonly var cmd = ref texts[command.Index];
                        var atlas = (VeldridTexture2D)cmd.Font.AtlasTexture;
                        SetTexture(ref currentRS, atlas.ResourceSet, context, ref quadCount);

                        AddTextToBatch(in cmd, context, currentRS, in camera, sw, sh, ref quadCount);
                        if (quadCount >= MaxQuads) Flush(context, currentRS, ref quadCount);
                        break;
                    }
                }
            }
        }

        if (quadCount > 0)
        {
            Flush(context, currentRS, ref quadCount);
        }

        context.CommandList.End();
    }

    private void AddRectToBatch(in DrawRectCmd cmd, in MergeContext context, in Camera2D camera, float sw, float sh, ref int quadCount)
    {
        Vector4 color = new Vector4(
            cmd.Color.R / 255f,
            cmd.Color.G / 255f,
            cmd.Color.B / 255f,
            cmd.Color.A / 255f
        );

        DrawTransform2D transform = new DrawTransform2D(
            new Vector2(cmd.Rectangle.X, cmd.Rectangle.Y),
            new Vector2(cmd.Rectangle.Width, cmd.Rectangle.Height),
            cmd.Origin,
            cmd.RotationRadians,
            cmd.Space);
        QuadTransform2D.BuildQuad(in transform, in camera, sw, sh, out Vector2 p0, out Vector2 p1, out Vector2 p2, out Vector2 p3);

        int offset = quadCount * 4;
        context.Vertices[offset + 0] = new Vertex2D
        {
            Position = p0,
            TexCoord = new Vector2(0, 0), Color = color
        };
        context.Vertices[offset + 1] = new Vertex2D
        {
            Position = p1,
            TexCoord = new Vector2(1, 0), Color = color
        };
        context.Vertices[offset + 2] = new Vertex2D
        {
            Position = p2,
            TexCoord = new Vector2(0, 1), Color = color
        };
        context.Vertices[offset + 3] = new Vertex2D
        {
            Position = p3,
            TexCoord = new Vector2(1, 1), Color = color
        };

        quadCount++;
    }

    private void AddTextureToBatch(in DrawTextureCmd cmd, in MergeContext context, in Camera2D camera, float sw, float sh, ref int quadCount)
    {
        Vector4 color = new Vector4(
            cmd.Color.R / 255f,
            cmd.Color.G / 255f,
            cmd.Color.B / 255f,
            cmd.Color.A / 255f
        );

        DrawTransform2D transform = new DrawTransform2D(
            cmd.Position,
            cmd.Size,
            cmd.Origin,
            cmd.RotationRadians,
            cmd.Space);
        QuadTransform2D.BuildQuad(in transform, in camera, sw, sh, out Vector2 p0, out Vector2 p1, out Vector2 p2, out Vector2 p3);
        TextureUvTransform.GetTextureCoords(cmd.SourceUv, out Vector2 uv0, out Vector2 uv1, out Vector2 uv2, out Vector2 uv3);

        int offset = quadCount * 4;
        context.Vertices[offset + 0] = new Vertex2D
        {
            Position = p0,
            TexCoord = uv0, Color = color
        };
        context.Vertices[offset + 1] = new Vertex2D
        {
            Position = p1,
            TexCoord = uv1, Color = color
        };
        context.Vertices[offset + 2] = new Vertex2D
        {
            Position = p2,
            TexCoord = uv2, Color = color
        };
        context.Vertices[offset + 3] = new Vertex2D
        {
            Position = p3,
            TexCoord = uv3, Color = color
        };

        quadCount++;
    }

    private void AddTextToBatch(in DrawTextCmd cmd, in MergeContext context, ResourceSet? currentRS, in Camera2D camera, float sw, float sh, ref int quadCount)
    {
        Vector4 color = new Vector4(
            cmd.Color.R / 255f,
            cmd.Color.G / 255f,
            cmd.Color.B / 255f,
            cmd.Color.A / 255f
        );

        TextLayoutResult layout = TextLayout.Build(cmd.Font, cmd.Text.Span, cmd.Size, context.TextGlyphs);
        Vector2 anchorOffset = TextAnchorTransform.GetOffset(cmd.Anchor, layout.Size);
        Vector2 textPosition = cmd.Position - anchorOffset;
        Vector2 textOrigin = anchorOffset + cmd.Origin;
        for (int i = 0; i < layout.GlyphCount; i++)
        {
            TextGlyphQuad glyph = context.TextGlyphs[i];
            Vector2 glyphPosition = textPosition + glyph.Position;
            Vector2 glyphOrigin = textOrigin - glyph.Position;
            DrawTransform2D transform = new DrawTransform2D(
                glyphPosition,
                glyph.Size,
                glyphOrigin,
                cmd.RotationRadians,
                cmd.Space);

            QuadTransform2D.BuildQuad(in transform, in camera, sw, sh, out Vector2 p0, out Vector2 p1, out Vector2 p2, out Vector2 p3);
            TextureUvTransform.GetTextureCoords(glyph.SourceUv, out Vector2 uv0, out Vector2 uv1, out Vector2 uv2, out Vector2 uv3);

            int offset = quadCount * 4;
            context.Vertices[offset + 0] = new Vertex2D
            {
                Position = p0,
                TexCoord = uv0, Color = color
            };
            context.Vertices[offset + 1] = new Vertex2D
            {
                Position = p1,
                TexCoord = uv1, Color = color
            };
            context.Vertices[offset + 2] = new Vertex2D
            {
                Position = p2,
                TexCoord = uv2, Color = color
            };
            context.Vertices[offset + 3] = new Vertex2D
            {
                Position = p3,
                TexCoord = uv3, Color = color
            };

            quadCount++;
            if (quadCount >= MaxQuads)
            {
                Flush(context, currentRS, ref quadCount);
            }
        }
    }

    private void SetPipeline(ref Pipeline? current, Pipeline next, MergeContext ctx, ResourceSet? currentRS, ref int quadCount)
    {
        if (current == next) return;

        Flush(ctx, currentRS, ref quadCount);
        current = next;
        ctx.CommandList.SetPipeline(next);
    }

    private void SetTexture(ref ResourceSet? current, ResourceSet next, MergeContext ctx, ref int quadCount)
    {
        if (current != next)
        {
            Flush(ctx, current, ref quadCount);
            current = next;
            ctx.CommandList.SetGraphicsResourceSet(0, next);
        }
    }

    private void Flush(MergeContext context, ResourceSet? rs, ref int quadCount)
    {
        if (quadCount == 0 || rs == null) return;

        uint sizeInBytes = (uint)(quadCount * 4 * Vertex2D.SizeInBytes);
        context.CommandList.UpdateBuffer(context.VertexBuffer, 0, ref context.Vertices[0], sizeInBytes);
        context.CommandList.DrawIndexed((uint)(quadCount * 6));

        quadCount = 0;
    }
}
