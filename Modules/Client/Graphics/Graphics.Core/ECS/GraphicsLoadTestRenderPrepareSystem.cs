using System.Drawing;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Graphics.Core;

public sealed class GraphicsLoadTestRenderPrepareSystem : ISystemRenderPrepare
{
    private const int Columns = 128;
    private const float QuadSize = 6f;
    private const float QuadSpacing = 7f;
    private static readonly Color StressColor = Color.Red;

    [DI] private GraphicsLoadTestSettings _settings = null!;

    public void RenderPrepare()
    {
        int quadCount = _settings.QuadCount;
        if (quadCount == 0)
        {
            return;
        }

        ICommandBuffer buffer = GraphicsContext.Buffer;
        for (int i = 0; i < quadCount; i++)
        {
            int column = i % Columns;
            int row = i / Columns;
            var command = new DrawRectCmd
            {
                Rectangle = new RectangleF(column * QuadSpacing, row * QuadSpacing, QuadSize, QuadSize),
                Color = StressColor,
                Space = DrawSpace.Screen
            };
            buffer.Add(in command);
        }
    }
}
