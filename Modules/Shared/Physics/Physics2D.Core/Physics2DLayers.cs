namespace Karpik.Engine.Shared.Physics.Core;

public enum Physics2DLayersEnum : uint
{
    All = uint.MaxValue,
    None = 0,
    Platform = 1 << 0,
    Player = 1 << 1
}

public static class Physics2DLayers
{
    public static readonly PhysicsLayerMask All = new((uint)Physics2DLayersEnum.All);
    public static readonly PhysicsLayerMask None = new((uint)Physics2DLayersEnum.None);
    public static readonly PhysicsLayerMask Platform = new((uint)Physics2DLayersEnum.Platform);
    public static readonly PhysicsLayerMask Player = new((uint)Physics2DLayersEnum.Player);
}