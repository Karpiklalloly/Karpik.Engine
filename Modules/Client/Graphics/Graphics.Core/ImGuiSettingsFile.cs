using ImGuiNET;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Karpik.Engine.Client.Graphics.Core;

internal sealed class ImGuiSettingsFile : IDisposable
{
    internal string FilePath { get; }
    private nint _filename;

    internal unsafe ImGuiSettingsFile(ImGuiIOPtr io, string applicationDirectory, string userDataDirectory)
    {
        // Never fall back to ImGui's relative filename, including on failed startup.
        io.NativePtr->IniFilename = null;
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataDirectory);
        if (!Path.IsPathFullyQualified(userDataDirectory))
            throw new ArgumentException("ImGui settings require an absolute user-data directory.", nameof(userDataDirectory));

        string host = Path.TrimEndingDirectorySeparator(Path.GetFullPath(applicationDirectory));
        if (OperatingSystem.IsWindows()) host = host.ToUpperInvariant();
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(host)));
        string directory = Path.Combine(userDataDirectory, "Karpik", "ImGui", key);
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, "imgui.ini");
        _filename = Marshal.StringToCoTaskMemUTF8(FilePath);
        io.NativePtr->IniFilename = (byte*)_filename;
        // The renderer may already have started its first frame using ImGui's default path.
        if (File.Exists(FilePath)) ImGui.LoadIniSettingsFromDisk(FilePath);
    }

    // The filename must outlive the native context's final settings save.
    public void Dispose() => Marshal.FreeCoTaskMem(Interlocked.Exchange(ref _filename, 0));
}
