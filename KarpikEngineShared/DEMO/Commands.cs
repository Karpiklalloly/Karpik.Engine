using System.Numerics;
using Karpik.Engine.Shared.DragonECS;
using Network;

namespace Karpik.Engine.Shared.DEMO;

public struct MoveCommand : IStateCommand
{
    public Vector2 Direction;
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