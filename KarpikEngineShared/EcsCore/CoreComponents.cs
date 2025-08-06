using Network;
using System.Numerics;
using System.Runtime.InteropServices;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Engine.Shared;

[Serializable]
[NetworkedComponent]
[StructLayout(LayoutKind.Explicit, Pack = 2, Size = 24)]
public struct Position : IEcsComponent
{
    [FieldOffset(0)]
    public Vector<double> Value;
    [NetworkedField][FieldOffset(0)]
    public double X;
    [NetworkedField][FieldOffset(8)]
    public double Y;
    [NetworkedField][FieldOffset(16)]
    public double Z;
}

[Serializable] [NetworkedComponent]
public struct Rotation : IEcsComponent
{
    [NetworkedField]
    public double Value;
}

[Serializable] [NetworkedComponent]
public struct Scale : IEcsComponent
{
    [NetworkedField]
    public double Value;
}

[Serializable] [NetworkedComponent]
public struct Speed : IEcsComponent
{
    [NetworkedField]
    public double Value;
}

[Serializable]
public struct GameInitEvent : IEcsComponentEvent
{
    public int Source { get; set; }
    public int Target { get; set; }
}

