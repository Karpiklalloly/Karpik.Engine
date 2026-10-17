namespace Karpik.Engine.Core;

public interface IRestartWorkerStateProvider
{
    public string Key { get; }
    
    public byte[] Capture();
    
    public void Restore(ReadOnlySpan<byte> data);
}