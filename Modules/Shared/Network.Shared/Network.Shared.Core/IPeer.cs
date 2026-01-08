namespace Karpik.Engine.Shared.Network.Core;

public interface IPeer
{
    public int Id { get; }
    public ConnectionState ConnectionState { get; }
    
    public void Send(IWriter writer, DeliveryMethod deliveryMethod);
}