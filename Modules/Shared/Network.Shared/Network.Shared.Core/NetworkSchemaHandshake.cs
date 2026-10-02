namespace Karpik.Engine.Shared.Network.Core;

public enum NetworkSchemaHandshakeResult : byte
{
    Accepted,
    SchemaMismatch,
}

public static class NetworkSchemaHandshake
{
    public static void Write(IWriter writer, long schemaHash)
    {
        writer.Reset();
        writer.Put((byte)PacketType.Handshake);
        writer.Put(schemaHash);
    }

    public static NetworkSchemaHandshakeResult ValidatePayload(
        IReader reader,
        long expectedSchemaHash,
        out long remoteSchemaHash)
    {
        remoteSchemaHash = reader.GetLong();
        return remoteSchemaHash == expectedSchemaHash
            ? NetworkSchemaHandshakeResult.Accepted
            : NetworkSchemaHandshakeResult.SchemaMismatch;
    }
}
