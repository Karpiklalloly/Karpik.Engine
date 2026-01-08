namespace Karpik.Engine.Shared.Network.Core;

public interface IRpc
{
    public IWriter GetWriter();
    public void Send(DeliveryMethod deliveryMethod);
}