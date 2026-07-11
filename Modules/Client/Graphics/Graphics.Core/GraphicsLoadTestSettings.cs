namespace Karpik.Engine.Client.Graphics.Core;

public sealed class GraphicsLoadTestSettings
{
    public const int MaxQuadCount = 8192;

    private int _quadCount;

    public int QuadCount
    {
        get => Volatile.Read(ref _quadCount);
        set => Volatile.Write(ref _quadCount, Math.Clamp(value, 0, MaxQuadCount));
    }
}
