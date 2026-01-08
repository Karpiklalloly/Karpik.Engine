using System.Numerics;
using System.Runtime.InteropServices;
using DCFApixels.DragonECS;
using Karpik.Engine.Shared.DragonECS;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.MyGame.Shared.Main;

[NetworkedComponent]
public struct Health : IEcsComponent
{
    [NetworkedField]
    public double Value;
}

public struct TookDamageEvent : IEcsComponent
{
    
}

[NetworkedComponent]
public struct Player : IEcsComponent;    

public struct LocalPlayer : IEcsComponent;

public struct MoveCommand : IStateCommand
{
    public Vector3 Direction;
    public int Source { get; set; }
    public int Target { get; set; }
}

public struct JumpCommand : IEventCommand
{
    public int Source { get; set; }
    public int Target { get; set; }
}

[Serializable]
public struct ShowMessageEvent : IEcsComponentEvent
{
    public int Source { get; set; }
    public int Target { get; set; }
    public string Message;
}

[Serializable]
public struct ShowVisualEffectEvent : IEcsComponentEvent
{
    public int Source { get; set; }
    public int Target { get; set; }
    public string EffectName;
    public Vector2 Position;
}

[Serializable]
public struct ReloadModsCommand : IEventCommand
{
    public int Source { get; set; }
    public int Target { get; set; }
}

public struct ShowMessageTargetRpc : ITargetRpcCommand
{
    public int Source { get; set; }
    public int Target { get; set; }
    public string Message;
}

public struct ShowEffectClientRpc : IClientRpcCommand
{
    public int Source { get; set; }
    public int Target { get; set; }
    public string Message;
}

public struct SetLocalPlayerTargetRpc : ITargetRpcCommand
{
    public int Source { get; set; }
    public int Target { get; set; }
    public int LocalPlayerNetId;
}

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