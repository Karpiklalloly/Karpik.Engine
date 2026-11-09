using System.Security.Cryptography;
using System.Text;

namespace Karpik.Content.Transport;

public enum ContentReloadTarget { Client = 1, Server = 2 }
public enum ContentReloadOutcome { Applied = 1, Superseded = 2, Failed = 3, Unknown = 4 }

public readonly record struct ContentReloadRequest(int ProtocolVersion, Guid RequestId, string OutputRoot,
    string Namespace, ContentReloadTarget Target, string GenerationId);
public readonly record struct ContentReloadResponse(int ProtocolVersion, Guid RequestId,
    ContentReloadOutcome Outcome, string? Error = null);

public readonly record struct ContentReloadAddress
{
    public string OutputRoot { get; }
    public string InstanceId { get; }
    public string Namespace { get; }
    public ContentReloadTarget Target { get; }
    public string PipeName { get; }
    internal string LockPath { get; }
    private ContentReloadAddress(string root, string instance, string ns, ContentReloadTarget target, string pipe, string path)
    { OutputRoot = root; InstanceId = instance; Namespace = ns; Target = target; PipeName = pipe; LockPath = path; }
    public static ContentReloadAddress Create(string outputRoot, string instanceId, string ns, ContentReloadTarget target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ns);
        if (outputRoot.Length > 4096 || instanceId.Length > 64 || ns.Length > 128 ||
            instanceId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_') ||
            ns.Any(char.IsControl) || target is not (ContentReloadTarget.Client or ContentReloadTarget.Server))
            throw new ArgumentException("Invalid or oversized Content endpoint identity.");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        string identity = OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root;
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity + "\0" + instanceId)));
        return new ContentReloadAddress(root, instanceId, ns, target, "karpik-content-" + hash,
            Path.Combine(root, ".content-reload-endpoints", hash + ".lock"));
    }
}
