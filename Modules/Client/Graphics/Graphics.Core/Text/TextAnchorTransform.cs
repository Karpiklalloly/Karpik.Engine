using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

internal static class TextAnchorTransform
{
    public static Vector2 GetOffset(TextAnchor anchor, Vector2 layoutSize)
    {
        float x = anchor switch
        {
            TextAnchor.TopCenter or TextAnchor.Center or TextAnchor.BottomCenter => layoutSize.X * 0.5f,
            TextAnchor.TopRight or TextAnchor.CenterRight or TextAnchor.BottomRight => layoutSize.X,
            _ => 0f
        };

        float y = anchor switch
        {
            TextAnchor.CenterLeft or TextAnchor.Center or TextAnchor.CenterRight => layoutSize.Y * 0.5f,
            TextAnchor.BottomLeft or TextAnchor.BottomCenter or TextAnchor.BottomRight => layoutSize.Y,
            _ => 0f
        };

        return new Vector2(x, y);
    }
}
