namespace Karpik.Engine.Shared.Network.Core;

[Flags]
public enum ConnectionState
{
    Outgoing = 2,
    Connected = 4,
    ShutdownRequested = 8,
    Disconnected = 16, // 0x10
    EndPointChange = 32, // 0x20
    Any = EndPointChange | ShutdownRequested | Connected | Outgoing, // 0x2E
}