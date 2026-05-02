using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

internal static class TextureUvTransform
{
    public static void GetTextureCoords(Vector4 sourceUv, out Vector2 uv0, out Vector2 uv1, out Vector2 uv2, out Vector2 uv3)
    {
        if (sourceUv.Z == 0f && sourceUv.W == 0f)
        {
            uv0 = new Vector2(0f, 0f);
            uv1 = new Vector2(1f, 0f);
            uv2 = new Vector2(0f, 1f);
            uv3 = new Vector2(1f, 1f);
            return;
        }

        uv0 = new Vector2(sourceUv.X, sourceUv.Y);
        uv1 = new Vector2(sourceUv.Z, sourceUv.Y);
        uv2 = new Vector2(sourceUv.X, sourceUv.W);
        uv3 = new Vector2(sourceUv.Z, sourceUv.W);
    }
}
