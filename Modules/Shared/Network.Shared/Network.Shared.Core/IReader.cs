namespace Karpik.Engine.Shared.Network.Core;

public interface IReader
{
    public int AvailableBytes { get; }
    
    public void Recycle();
    
    public float GetFloat();
    public byte GetByte();
    public ushort GetUShort();
    public int GetInt();
    public long GetLong();
    public double GetDouble();
    public bool GetBool();
    public string GetString();
}