# New RPC / Network Feature

**Purpose**: Create a new network command or synced component for KarpikEngine

## Context

KarpikEngine has multiple networking patterns:
1. **Commands** — Client↔Server communication
2. **Networked Components** — automatic sync between Client and Server
3. **RPC** — direct peer-to-peer or broadcast messages

## Commands (Client↔Server)

### Command Types

| Interface | Pattern | Use Case |
|-----------|---------|----------|
| `IEventCommand` | Fire-and-forget | One-time events |
| `IStateCommand` | Request-response | Commands expecting response |
| `ITargetRpcCommand` | To specific peer | Private message to one client |
| `IClientRpcCommand` | Broadcast | Message to all clients |

### Input

- **Command name**: e.g., SpawnEntityCommand, ChatMessageRpc
- **Type**: Choose from table above
- **Fields**: What data is sent?

### Command Structure

```csharp
// Event: fire-and-forget
public struct PlayerJumpCommand : IEventCommand
{
    public int Source { get; set; }
    public int Target { get; set; }
    public float Force;
}

// State: request-response (generates Request struct automatically)
public struct MoveCommand : IStateCommand
{
    public Vector3 Direction;
    public int Source { get; set; }
    public int Target { get; set; }
}

// Target RPC: to specific peer
public struct ShowMessageTargetRpc : ITargetRpcCommand
{
    public int Source { get; set; }
    public int Target { get; set; }
    public string Message;
}

// Client RPC: broadcast to all clients
public struct ShowEffectClientRpc : IClientRpcCommand
{
    public int Source { get; set; }
    public int Target { get; set; }
    public string EffectName;
}
```

### Usage

```csharp
[DI] private Rpc _rpc;

// Extension method generated automatically for IEventCommand/IStateCommand
_rpc.PlayerJump(new PlayerJumpCommand 
{ 
    Target = targetId, 
    Force = 10.0f 
});

// For ITargetRpcCommand - send to specific peer
_rpc.SendTargetRpc(new ShowMessageTargetRpc { Target = peerId, Message = "Hi!" });

// For IClientRpcCommand - broadcast
_rpc.SendClientRpc(new ShowEffectClientRpc { Target = 0, EffectName = "explosion" });
```

### Server Handling (CommandDispatcher)

```csharp
partial class CommandDispatcher
{
    [DI] private EcsEventWorld _eventWorld;
    
    public void Dispatch(int playerEntity, IReader reader)
    {
        var commandId = reader.GetUShort();
        switch (commandId)
        {
            case 1: // PlayerJumpCommand
            {
                var cmd = new PlayerJumpCommand();
                cmd.Force = reader.GetFloat();
                _eventWorld.SendEvent(cmd);
                break;
            }
        }
    }
}
```

---

## Networked Components (Automatic Sync)

For data that needs automatic synchronization between Server and Client.

### Input

- **Component name**: e.g., Health, Position, Inventory
- **Fields to sync**: Which fields should be sent over network?

### Structure

```csharp
// Entire component synced
[NetworkedComponent]
public struct Health : IEcsComponent
{
    [NetworkedField]
    public float Value;
}

// Selective fields synced
[NetworkedComponent]
public struct Position : IEcsComponent
{
    [NetworkedField]
    public float X;
    [NetworkedField]
    public float Y;
    [NetworkedField]
    public float Z;
    public float _localOnly; // not synced - no [NetworkedField]
}

// Component without sync (just for ECS)
public struct Velocity : IEcsComponent
{
    public float X, Y;
}
```

### Generated Code

Network.Codegen generates sync code automatically:
- Serialization methods for each networked component
- Delta compression where possible
- Automatic sending on change detection

---

## ECS Events (Local Only)

For internal events within ECS, not sent over network.

```csharp
// Not serialized, used within ECS pipeline
public struct TookDamageEvent : IEcsComponentEvent
{
    public int Source { get; set; }
    public int Target { get; set; }
    public float Damage;
}

// With [Serializable] - can be used in some serialization contexts
[Serializable]
public struct ShowMessageEvent : IEcsComponentEvent
{
    public int Source { get; set; }
    public int Target { get; set; }
    public string Message;
}
```

---

## Constraints

### Commands
- **MUST** implement appropriate interface (`IEventCommand`, `IStateCommand`, `ITargetRpcCommand`, `IClientRpcCommand`)
- **DO NOT** send classes — only structs
- **DO** use simple serializable types (int, float, string, arrays)
- Commands go in Shared — available to both Client and Server

### Networked Components
- **MUST** have `[NetworkedComponent]` attribute on the struct
- Fields to sync **MUST** have `[NetworkedField]` attribute
- Non-networked fields should NOT have the attribute
- Networked components are automatically handled by `NetworkGenerator`

### General
- **DO** use throttle in Update — don't send commands every frame without limits
- Command ID is assigned automatically by `CommandIdManager`
- All networked data flows through Network.Codegen — no manual serialization needed