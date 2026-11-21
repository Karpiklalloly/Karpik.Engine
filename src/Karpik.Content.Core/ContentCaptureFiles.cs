using System.Runtime.InteropServices;

namespace Karpik.Content.Core;

/// <summary>Read-only filesystem operations for an ordinary packaged Content install.</summary>
public static class ContentCaptureFiles
{
    public const int MaximumMetadataBytes = 4 * 1024 * 1024;

    public static bool DirectoryExists(string path)
    {
        bool exists = true;
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if (Attributes(current) is { } attributes) RequireOrdinary(current, attributes, directory: true);
            else exists = false;
        }
        return exists;
    }

    public static FileStream? OpenRead(string path)
    {
        if (!DirectoryExists(Path.GetDirectoryName(path)!)) return null;
        if (Attributes(path) is not { } attributes) return null;
        RequireOrdinary(path, attributes, directory: false);
        // Unix FileAttributes does not distinguish a FIFO/device/socket from a regular file.
        if (OperatingSystem.IsLinux()) RequireRegularLinuxFile(path);
        else if (!OperatingSystem.IsWindows()) throw new InvalidDataException("Safe mod Content capture is supported on Linux and Windows.");
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public static byte[]? ReadMetadata(string path, CancellationToken ct = default, int maximumBytes = MaximumMetadataBytes)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = OpenRead(path);
        if (stream is null) return null;
        long length = stream.Length;
        if (length > maximumBytes) throw new InvalidDataException($"Content metadata exceeds {maximumBytes} bytes: '{path}'.");
        var bytes = new byte[(int)length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException($"Content metadata changed while reading: '{path}'.");
        ct.ThrowIfCancellationRequested();
        return bytes;
    }

    public static string ResolveArtifact(string root, string locator)
    {
        if (locator is null || !locator.StartsWith("artifacts/", StringComparison.Ordinal) ||
            locator.Split('/').Any(segment => !PathSafety.IsSafeLeafName(segment) || segment.EndsWith('.') || segment.EndsWith(' ') || segment.Contains(':')))
            throw new InvalidDataException($"Unsafe cooked artifact locator '{locator}'.");
        string path = Path.GetFullPath(Path.Combine(root, locator));
        if (!PathSafety.IsContained(root, path)) throw new InvalidDataException($"Cooked artifact locator escapes the generation: '{locator}'.");
        return path;
    }

    private static void RequireRegularLinuxFile(string path)
    {
        const int currentDirectory = -100, noFollow = 0x100, noAutomount = 0x800;
        const uint statType = 1;
        LinuxStat stat;
        try
        {
            if (Statx(currentDirectory, path, noFollow | noAutomount, statType, out stat) != 0)
                throw new IOException($"Cannot inspect Content file '{path}' (errno {Marshal.GetLastPInvokeError()}).");
        }
        catch (EntryPointNotFoundException error)
        {
            throw new InvalidDataException("Safe mod Content capture requires Linux statx.", error);
        }
        if ((stat.Mask & statType) == 0 || (stat.Mode & 0xf000) != 0x8000)
            throw new InvalidDataException($"Expected an ordinary Content file: '{path}'.");
    }

    // Linux statx has a fixed layout on all supported architectures; only the mask and file mode are needed.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStat
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(28)] public ushort Mode;
    }

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int dirFd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out LinuxStat result);

    private static FileAttributes? Attributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }

    private static void RequireOrdinary(string path, FileAttributes attributes, bool directory)
    {
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0 || attributes.HasFlag(FileAttributes.Directory) != directory)
            throw new InvalidDataException($"Expected an ordinary Content {(directory ? "directory" : "file")}: '{path}'.");
    }
}
