using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Karpik.Content.Transport;

internal static class ContentReloadWire
{
    internal const int Version = 1;
    internal const int MaximumMessageBytes = 16 * 1024;
    internal const int MaximumErrorLength = 2048;
    internal static string? ValidateRequest(ContentReloadAddress address, ContentReloadRequest request)
    {
        if (request.ProtocolVersion != Version) return "Unsupported Content reload protocol version.";
        if (request.RequestId == Guid.Empty) return "A nonempty request identifier is required.";
        if (request.Namespace is null || request.Namespace.Length > 128 || request.Namespace != address.Namespace)
            return "Content namespace does not match endpoint.";
        if (request.Target != address.Target) return "Content target does not match endpoint.";
        if (request.OutputRoot is null || request.OutputRoot.Length > 4096) return "Invalid output root.";
        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.OutputRoot));
            if (!string.Equals(root, address.OutputRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return "Content output root does not match endpoint.";
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException)
        { return "Invalid output root."; }
        if (request.GenerationId is null || request.GenerationId.Length != 32 ||
            !Guid.TryParseExact(request.GenerationId, "N", out Guid generation) || generation == Guid.Empty ||
            generation.ToString("N") != request.GenerationId)
            return "A canonical nonempty generation identifier is required.";
        return null;
    }
    internal static void ValidateAddress(ContentReloadAddress address)
    {
        if (address != ContentReloadAddress.Create(address.OutputRoot, address.InstanceId, address.Namespace, address.Target))
            throw new ArgumentException("Invalid Content endpoint address.");
    }
    internal static bool ValidResponse(ContentReloadResponse response, Guid requestId) =>
        response.ProtocolVersion == Version && response.RequestId == requestId &&
        response.Outcome is ContentReloadOutcome.Applied or ContentReloadOutcome.Superseded or ContentReloadOutcome.Failed or ContentReloadOutcome.Unknown &&
        (response.Error is null || response.Error.Length <= MaximumErrorLength);
    internal static ContentReloadResponse Failure(Guid id, string error) => new(Version, id, ContentReloadOutcome.Failed, Bound(error));
    internal static ContentReloadResponse Unknown(Guid id, string error) => new(Version, id, ContentReloadOutcome.Unknown, Bound(error));
    private static string Bound(string error) => error.Length <= MaximumErrorLength ? error : error[..MaximumErrorLength];
    internal static async Task<byte[]> ReadAsync(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        int size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size <= 0 || size > MaximumMessageBytes) throw new InvalidDataException("Invalid Content message length.");
        byte[] payload = new byte[size];
        await stream.ReadExactlyAsync(payload, token).ConfigureAwait(false);
        return payload;
    }
    internal static async Task WriteAsync(Stream stream, byte[] payload, CancellationToken token)
    {
        if (payload.Length <= 0 || payload.Length > MaximumMessageBytes) throw new InvalidDataException("Content message exceeds its size limit.");
        byte[] header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        await stream.WriteAsync(payload, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }
}

[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ContentReloadRequest))]
[JsonSerializable(typeof(ContentReloadResponse))]
internal partial class ContentReloadJsonContext : JsonSerializerContext;
