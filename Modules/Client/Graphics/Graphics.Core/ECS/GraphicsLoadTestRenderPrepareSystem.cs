using System.Drawing;
using System.Numerics;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Graphics.Core;

public sealed class GraphicsLoadTestRenderPrepareSystem : ISystemRenderPrepare
{
    private const int Columns = 128;
    private const float QuadSize = 6f;
    private const float QuadSpacing = 7f;
    private const int TextureCount = 4;
    private const int TextureBatchQuadCount = 64;
    private static readonly Color StressColor = Color.Red;

    [DI] private GraphicsLoadTestSettings _settings = null!;
    [DI] private GraphicsLoadTestResources _resources = null!;

    public void RenderPrepare()
    {
        int quadCount = _settings.QuadCount;
        if (quadCount == 0)
        {
            return;
        }

        ICommandBuffer buffer = GraphicsContext.Buffer;
        GraphicsLoadTestScenario scenario = _settings.Scenario;
        for (int i = 0; i < quadCount; i++)
        {
            int column = i % Columns;
            int row = i / Columns;
            var rectangle = new RectangleF(column * QuadSpacing, row * QuadSpacing, QuadSize, QuadSize);
            switch (scenario)
            {
                case GraphicsLoadTestScenario.SortedRects:
                    AddRect(buffer, rectangle, sortKey: (ulong)i);
                    break;
                case GraphicsLoadTestScenario.UnsortedRects:
                    AddRect(buffer, rectangle, sortKey: (ulong)(quadCount - i));
                    break;
                case GraphicsLoadTestScenario.TextureBatches:
                    AddTexture(buffer, rectangle, (i / TextureBatchQuadCount) % TextureCount, (ulong)i);
                    break;
                case GraphicsLoadTestScenario.TextureThrash:
                    AddTexture(buffer, rectangle, i % TextureCount, (ulong)i);
                    break;
            }
        }
    }

    private static void AddRect(ICommandBuffer buffer, RectangleF rectangle, ulong sortKey)
    {
        var command = new DrawRectCmd
        {
            Rectangle = rectangle,
            Color = StressColor,
            Space = DrawSpace.Screen,
            SortKey = sortKey
        };
        buffer.Add(in command);
    }

    private void AddTexture(ICommandBuffer buffer, RectangleF rectangle, int textureIndex, ulong sortKey)
    {
        buffer.AddTexture(
            _resources.GetTexture(textureIndex),
            new Vector2(rectangle.X, rectangle.Y),
            new Vector2(rectangle.Width, rectangle.Height),
            Color.White,
            space: DrawSpace.Screen,
            sortKey: sortKey);
    }
}
