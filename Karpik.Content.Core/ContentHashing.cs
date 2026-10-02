using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Karpik.Content.Core;

public static class ContentHashing
{
    private static readonly byte[] FormatPrefix = Encoding.ASCII.GetBytes("KARPIK-CONTENT-ARTIFACT\0v1");
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string HashBytes(ReadOnlySpan<byte> data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string HashString(string text)
    {
        return HashBytes(Encoding.UTF8.GetBytes(text));
    }

    public static string HashSourceBytes(ReadOnlySpan<byte> sourceBytes) => HashBytes(sourceBytes);

    public static string HashImportSettings(string canonicalImportSettingsJson) => HashString(canonicalImportSettingsJson);

    public static string ComputeArtifactHash(ReadOnlySpan<byte> sourceBytes, string canonicalMetaJson, string processorVersion, AssetTarget target)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(FormatPrefix);

        Span<byte> frame = stackalloc byte[sizeof(ulong)];

        string targetName = target switch
        {
            AssetTarget.Client => "Client",
            AssetTarget.Server => "Server",
            AssetTarget.Shared => "Shared",
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown content target.")
        };

        AppendFramed(hash, frame, "source", sourceBytes);
        AppendFramed(hash, frame, "meta", StrictUtf8.GetBytes(canonicalMetaJson));
        AppendFramed(hash, frame, "processorVersion", StrictUtf8.GetBytes(processorVersion));
        AppendFramed(hash, frame, "target", StrictUtf8.GetBytes(targetName));

        byte[] digest = hash.GetHashAndReset();
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    public static string ComputeArtifactLocator(string artifactHash)
    {
        // Use two-level sharding: artifacts/ab/cd/<hash>.cooked  but keep simple: artifacts/<hash>.cooked
        // To keep opaque but deterministic, use artifacts/<first2>/<hash>.bin
        string sub1 = artifactHash.Substring(0, 2);
        string sub2 = artifactHash.Substring(2, 2);
        return $"artifacts/{sub1}/{sub2}/{artifactHash}.cooked";
    }

    private static void AppendFramed(IncrementalHash hash, Span<byte> frame, string fieldName, ReadOnlySpan<byte> value)
    {
        byte[] nameBytes = StrictUtf8.GetBytes(fieldName);
        BinaryPrimitives.WriteUInt64BigEndian(frame, (ulong)nameBytes.Length);
        hash.AppendData(frame);
        hash.AppendData(nameBytes);

        BinaryPrimitives.WriteUInt64BigEndian(frame, (ulong)value.Length);
        hash.AppendData(frame);
        if (value.Length > 0)
        {
            hash.AppendData(value);
        }
    }
}