namespace Karpik.Engine.Client.Graphics.Core;

public static class DrawSortKey
{
    public static ulong FromLayer(int layer)
    {
        return unchecked((uint)(layer - int.MinValue));
    }

    public static ulong FromLayerDescending(int layer)
    {
        return uint.MaxValue - FromLayer(layer);
    }
}
