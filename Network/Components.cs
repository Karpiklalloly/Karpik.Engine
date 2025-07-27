using DCFApixels.DragonECS;
using Karpik.Engine.Shared.DragonECS;

namespace Network;

public interface INetCommand : IEcsComponentEvent;
public interface IStateCommand : INetCommand;
public interface IEventCommand : INetCommand;

public interface ITargetRpcCommand : INetCommand;
public interface IClientRpcCommand : INetCommand;

public struct NetworkId : IEcsComponent
{
    public int Id;
}