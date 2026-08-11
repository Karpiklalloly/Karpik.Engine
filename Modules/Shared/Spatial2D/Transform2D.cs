using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.Shared.Spatial2D;

[NetworkedComponent]
public struct Transform2D : IEcsComponent 
{
    [NetworkedField]
    public Vector2 Position;
    [NetworkedField]
    public float Rotation;
        
    public Vector2 Forward => new Vector2(MathF.Cos(Rotation), MathF.Sin(Rotation));
}