using ImGuiNET;
using Xunit;

namespace Karpik.Engine.Client.Graphics.Core.Tests;

[CollectionDefinition("ImGui settings", DisableParallelization = true)]
public sealed class ImGuiSettingsCollection { }

[Collection("ImGui settings")]
public sealed class ImGuiSettingsFileTests
{
    [Fact]
    public void Native_settings_survive_context_recreation_without_writing_into_bundle()
    {
        string root = Path.Combine(Path.GetTempPath(), "karpik-imgui-" + Guid.NewGuid().ToString("N"));
        string bundle = Path.Combine(root, "bundle");
        string userData = Path.Combine(root, "user-data");
        string host = Path.Combine(root, "client-launcher");
        Directory.CreateDirectory(bundle);
        string previousDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = bundle;
            string savedPath = WithSettings(host, userData, settings =>
            {
                Assert.True(Path.IsPathFullyQualified(settings.FilePath));
                Assert.StartsWith(userData + Path.DirectorySeparatorChar, settings.FilePath);
                Assert.Equal(settings.FilePath, ImGui.GetIO().IniFilename.ToString());
                ImGui.LoadIniSettingsFromMemory("[Window][Lifecycle probe]\nPos=25,35\nSize=300,200\nCollapsed=0\n\n");
                ImGui.SaveIniSettingsToDisk(settings.FilePath);
            });
            Assert.True(File.Exists(savedPath));
            WithSettings(host, userData, settings =>
            {
                Assert.Equal(savedPath, settings.FilePath);
                Assert.Contains("[Window][Lifecycle probe]", ImGui.SaveIniSettingsToMemory());
            });
            Assert.False(File.Exists(Path.Combine(bundle, "imgui.ini")));
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Settings_are_scoped_to_the_host_directory_and_normalize_trailing_separators()
    {
        string root = Path.Combine(Path.GetTempPath(), "karpik-imgui-" + Guid.NewGuid().ToString("N"));
        try
        {
            string userData = Path.Combine(root, "data");
            string host = Path.Combine(root, "client");
            string first = WithSettings(host, userData, _ => { });
            string same = WithSettings(host + Path.DirectorySeparatorChar, userData, _ => { });
            string other = WithSettings(Path.Combine(root, "another-client"), userData, _ => { });
            Assert.Equal(first, same);
            Assert.NotEqual(first, other);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static string WithSettings(string host, string userData, Action<ImGuiSettingsFile> check)
    {
        nint context = ImGui.CreateContext();
        ImGuiSettingsFile? settings = null;
        try
        {
            settings = new ImGuiSettingsFile(ImGui.GetIO(), host, userData);
            check(settings);
            return settings.FilePath;
        }
        finally
        {
            try { ImGui.DestroyContext(context); }
            finally { settings?.Dispose(); }
        }
    }
}
