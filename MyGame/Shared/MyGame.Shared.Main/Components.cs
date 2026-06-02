using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Shared.DragonECS;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.MyGame.Shared.Main;

[NetworkedComponent]
public struct Health : IEcsComponent
{
    [NetworkedField]
    public float Value;
}

public struct TookDamageEvent : IEcsComponent
{
    
}

[NetworkedComponent]
public struct Player : IEcsComponent;    

public struct LocalPlayer : IEcsComponent;

public struct PlayerSession : IEcsComponent
{
    public long ReconnectToken;
}

public struct ClientReconnectSession : IEcsComponent
{
    public long ReconnectToken;
}

public struct PlayerConnection : IEcsComponent
{
    public int PeerId;
    public bool Connected;
}

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
    public long ReconnectToken;
}

[Serializable] [NetworkedComponent]
public struct Position : IEcsComponent
{
    [NetworkedField]
    public float X;
    [NetworkedField]
    public float Y;
    [NetworkedField]
    public float Z;
}

[Serializable] [NetworkedComponent]
public struct Speed : IEcsComponent
{
    [NetworkedField]
    public float Value;
}

[Serializable]
public struct GameInitEvent : IEcsComponentEvent
{
    public int Source { get; set; }
    public int Target { get; set; }
}
