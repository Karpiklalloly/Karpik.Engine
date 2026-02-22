using System.Text;

namespace Karpik.Engine.Core;

/// <summary>
/// Message types for IPC communication between Watcher and Worker processes.
/// </summary>
public enum IpcMessageType : byte
{
    // Connection management
    PingRequest = 0x01,
    PingResponse = 0x02,
    
    // State transfer
    StateRequest = 0x10,
    StateResponse = 0x11,
    
    // Shutdown
    ShutdownRequest = 0x20,
    ShutdownAck = 0x21,
    
    // Hot reload
    HotReloadPrepare = 0x30,
    HotReloadReady = 0x31,
    HotReloadRequest = 0x32,  // Worker requests hot reload from Watcher
    
    // Worker ready signal
    WorkerReady = 0x40,
}

/// <summary>
/// IPC message structure.
/// Format: [4 bytes length][1 byte type][N bytes payload]
/// </summary>
public readonly struct IpcMessage
{
    public IpcMessageType Type { get; }
    public byte[] Payload { get; }
    
    public IpcMessage(IpcMessageType type, byte[]? payload = null)
    {
        Type = type;
        Payload = payload ?? Array.Empty<byte>();
    }
    
    public byte[] ToBytes()
    {
        var buffer = new byte[5 + Payload.Length];
        var lengthBytes = BitConverter.GetBytes(Payload.Length);
        Buffer.BlockCopy(lengthBytes, 0, buffer, 0, 4);
        buffer[4] = (byte)Type;
        Buffer.BlockCopy(Payload, 0, buffer, 5, Payload.Length);
        return buffer;
    }
    
    public static IpcMessage FromBytes(byte[] buffer, int offset = 0)
    {
        var payloadLength = BitConverter.ToInt32(buffer, offset);
        var type = (IpcMessageType)buffer[offset + 4];
        var payload = new byte[payloadLength];
        Buffer.BlockCopy(buffer, offset + 5, payload, 0, payloadLength);
        return new IpcMessage(type, payload);
    }
    
    public static (int totalLength, IpcMessageType type) ReadHeader(byte[] buffer, int offset = 0)
    {
        var payloadLength = BitConverter.ToInt32(buffer, offset);
        var type = (IpcMessageType)buffer[offset + 4];
        return (5 + payloadLength, type);
    }
}

public class HotReloadState
{
    public Dictionary<string, byte[]> ModuleStates { get; set; } = new();
    public long Timestamp { get; set; }
    
    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        
        writer.Write(Timestamp);
        writer.Write(ModuleStates.Count);
        
        foreach (var (key, value) in ModuleStates)
        {
            writer.Write(key);
            writer.Write(value.Length);
            writer.Write(value);
        }
        
        return ms.ToArray();
    }
    
    public static HotReloadState Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        
        var state = new HotReloadState
        {
            Timestamp = reader.ReadInt64()
        };
        
        var count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            var key = reader.ReadString();
            var length = reader.ReadInt32();
            var value = reader.ReadBytes(length);
            state.ModuleStates[key] = value;
        }
        
        return state;
    }
}
