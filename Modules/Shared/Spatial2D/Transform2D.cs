using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Network.Core;
using OpenTK.Mathematics;

namespace Karpik.Engine.Shared.Spatial2D;


[NetworkedComponent]
public struct Transform2D : IEcsComponent 
{
    [NetworkedField]
    public Vector2d Position;
    [NetworkedField]
    public float Rotation;
    [NetworkedField]
    public Vector2 Scale;
    
    public Vector2d Forward => new Vector2d(Math.Cos(Rotation), Math.Sin(Rotation));
}