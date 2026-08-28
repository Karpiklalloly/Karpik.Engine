using Karpik.Content.Core;
using Xunit;

namespace Karpik.Content.Tests;

public sealed class RecoveryAndToolTests
{
    [Fact]
    public void Recover_KeepsWrapperWhenCopyFails()
    {
        using var tmp = new TemporaryDirectory();
        string output = tmp.CreateSubdirectory("output");
        Directory.CreateDirectory(output);
        // Simulate a previous publish that left a journal and backup
        string replacement = Path.Combine(output, ".replacement");
        string tx = Guid.NewGuid().ToString("N");
        string wrapper = Path.Combine(replacement, tx);
        string payload = Path.Combine(wrapper, "payload");
        Directory.CreateDirectory(payload);
        string journal = Path.Combine(wrapper, ".journal");
        File.WriteAllText(journal, "started");
        string backupManifest = Path.Combine(payload, "manifest.json");
        File.WriteAllText(backupManifest, """{"schemaVersion":1,"entries":[]}""");
        // Create current manifest as invalid to trigger restore attempt, but lock backup to make Copy fail
        string currentManifest = Path.Combine(output, "manifest.json");
        File.WriteAllText(currentManifest, "invalid json");

        // Lock backup file exclusively to force File.Copy to fail (IOException)
        using (var lockStream = new FileStream(backupManifest, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var publisher = new ContentAtomicPublisher(output);
            publisher.Recover();
            // After failed copy, journal and wrapper must remain (not deleted)
            Assert.True(File.Exists(journal), "journal should remain after failed copy");
            Assert.True(Directory.Exists(wrapper), "wrapper should remain after failed copy");
            Assert.True(File.Exists(backupManifest), "backup should remain");
        }

        // After releasing lock, next Recover should succeed and clean up
        var publisher2 = new ContentAtomicPublisher(output);
        publisher2.Recover();
        // Now current manifest was invalid, so it should have been restored from backup and wrapper cleaned
        Assert.True(File.Exists(currentManifest));
        string content = File.ReadAllText(currentManifest);
        Assert.Contains("schemaVersion", content);
        // Wrapper should be cleaned after successful restore (or at least journal removed)
        // Note: our Recover deletes wrapper recursively after success, so it should be gone
        Assert.False(Directory.Exists(wrapper), "wrapper should be cleaned after successful restore");
    }

    [Fact]
    public void SdkProps_ToolBuildAlwaysInvokesMSBuild()
    {
        string sdkPropsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Karpik.Engine.Sdk", "Sdk", "Sdk.props"));
        string sdkProps = File.ReadAllText(sdkPropsPath);
        // Should contain KarpikContentToolBuild without !Exists(ToolDll) condition
        Assert.Contains("KarpikContentToolBuild", sdkProps);
        Assert.DoesNotContain("!Exists('$(KarpikContentToolDll)')", sdkProps);
        Assert.Contains("DependsOnTargets=\"KarpikContentToolBuild\"", sdkProps);
        // Should use MSBuildThisFileDirectory for tool path
        Assert.Contains("KarpikContentToolDll", sdkProps);
        Assert.Contains("MSBuildThisFileDirectory", sdkProps);
    }

    [Fact]
    public void Canonical_LargeNumbers_Normalized()
    {
        // 1.0e400 and 10e399 should be same canonical (both 1e400)
        string json1 = """{"v": 1.0e400}""";
        string json2 = """{"v": 10e399}""";
        using var doc1 = System.Text.Json.JsonDocument.Parse(json1);
        using var doc2 = System.Text.Json.JsonDocument.Parse(json2);
        string c1 = CanonicalJson.SerializeCanonical(doc1.RootElement);
        string c2 = CanonicalJson.SerializeCanonical(doc2.RootElement);
        Assert.Equal(c1, c2);
        Assert.Equal("""{"v":1e400}""", c1);

        // 1e400 vs 1E400 should be same (lowercase)
        string json3 = """{"v": 1e400}""";
        string json4 = """{"v": 1E400}""";
        using var doc3 = System.Text.Json.JsonDocument.Parse(json3);
        using var doc4 = System.Text.Json.JsonDocument.Parse(json4);
        string c3 = CanonicalJson.SerializeCanonical(doc3.RootElement);
        string c4 = CanonicalJson.SerializeCanonical(doc4.RootElement);
        Assert.Equal(c3, c4);
    }
}
