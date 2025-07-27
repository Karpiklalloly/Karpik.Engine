using Network;

namespace Karpik.Engine.Shared.DEMO;

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