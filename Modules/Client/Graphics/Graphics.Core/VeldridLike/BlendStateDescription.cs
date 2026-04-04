namespace Karpik.Engine.Client.Graphics.Core.VeldridLike;

public readonly struct BlendStateDescription
{
    public readonly BlendFactor SourceColorFactor;
    public readonly BlendFactor DestinationColorFactor;
    public readonly BlendFunction ColorFunction;
    
    public readonly BlendFactor SourceAlphaFactor;
    public readonly BlendFactor DestinationAlphaFactor;
    public readonly BlendFunction AlphaFunction;

    public BlendStateDescription(
        BlendFactor srcColor, BlendFactor dstColor, BlendFunction colorFunc,
        BlendFactor srcAlpha, BlendFactor dstAlpha, BlendFunction alphaFunc)
    {
        SourceColorFactor = srcColor; DestinationColorFactor = dstColor; ColorFunction = colorFunc;
        SourceAlphaFactor = srcAlpha; DestinationAlphaFactor = dstAlpha; AlphaFunction = alphaFunc;
    }

    // Для непрозрачных объектов
    public static readonly BlendStateDescription Opaque = new(
        BlendFactor.One, BlendFactor.Zero, BlendFunction.Add,
        BlendFactor.One, BlendFactor.Zero, BlendFunction.Add);

    // Стандартная полупрозрачность (Альфа-блендинг)
    public static readonly BlendStateDescription AlphaBlend = new(
        BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha, BlendFunction.Add,
        BlendFactor.One, BlendFactor.InverseSourceAlpha, BlendFunction.Add);
}