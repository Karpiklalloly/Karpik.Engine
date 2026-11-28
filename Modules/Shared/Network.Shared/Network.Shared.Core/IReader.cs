using System.Drawing;

namespace Karpik.Engine.Shared.Network.Core;

public interface IReader
{
    public int AvailableBytes { get; }

    /// <summary>Copy this transport-validated value into deferred work, not the reader; zero denotes unversioned gameplay.</summary>
    public long GameplayRevision => 0;
    
    public void Recycle();
    
    public float GetFloat();
    public byte GetByte();
    public ushort GetUShort();
    public int GetInt();
    public long GetLong();
    public double GetDouble();
    public bool GetBool();
    public string GetString();
    public Color GetColor();
}