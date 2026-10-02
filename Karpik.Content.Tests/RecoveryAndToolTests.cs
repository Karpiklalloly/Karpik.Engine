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
    public void SdkProps_UsesPackagedContentTool()
    {
        string sdkPropsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Karpik.Engine.Sdk", "Sdk", "Sdk.props"));
        string sdkProps = File.ReadAllText(sdkPropsPath);
        Assert.DoesNotContain("KarpikContentToolBuild", sdkProps);
        Assert.DoesNotContain("Karpik.Content.Tool.csproj", sdkProps);
        Assert.Contains("tools\\net10.0\\content\\content.dll", sdkProps);
        Assert.Contains("$(DOTNET_HOST_PATH)", sdkProps);
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

        // 0.001e400 => 1e397, 0.00012e400 => 1.2e396
        string json5 = """{"v": 0.001e400}""";
        string json6 = """{"v": 1e397}""";
        using var doc5 = System.Text.Json.JsonDocument.Parse(json5);
        using var doc6 = System.Text.Json.JsonDocument.Parse(json6);
        string c5 = CanonicalJson.SerializeCanonical(doc5.RootElement);
        string c6 = CanonicalJson.SerializeCanonical(doc6.RootElement);
        Assert.Equal(c5, c6);
        Assert.Equal("""{"v":1e397}""", c5);

        string json7 = """{"v": 0.00012e400}""";
        string json8 = """{"v": 1.2e396}""";
        using var doc7 = System.Text.Json.JsonDocument.Parse(json7);
        using var doc8 = System.Text.Json.JsonDocument.Parse(json8);
        string c7 = CanonicalJson.SerializeCanonical(doc7.RootElement);
        string c8 = CanonicalJson.SerializeCanonical(doc8.RootElement);
        Assert.Equal(c7, c8);
        Assert.Equal("""{"v":1.2e396}""", c7);

        // Negative
        string json9 = """{"v": -0.001e400}""";
        string json10 = """{"v": -1e397}""";
        using var doc9 = System.Text.Json.JsonDocument.Parse(json9);
        using var doc10 = System.Text.Json.JsonDocument.Parse(json10);
        string c9 = CanonicalJson.SerializeCanonical(doc9.RootElement);
        string c10 = CanonicalJson.SerializeCanonical(doc10.RootElement);
        Assert.Equal(c9, c10);
        Assert.Equal("""{"v":-1e397}""", c9);

        // Very large exponent outside Int32: 1e2147483648 vs 10e2147483647
        string json11 = """{"v": 1e2147483648}""";
        string json12 = """{"v": 10e2147483647}""";
        using var doc11 = System.Text.Json.JsonDocument.Parse(json11);
        using var doc12 = System.Text.Json.JsonDocument.Parse(json12);
        string c11 = CanonicalJson.SerializeCanonical(doc11.RootElement);
        string c12 = CanonicalJson.SerializeCanonical(doc12.RootElement);
        Assert.Equal(c11, c12);
        Assert.Equal("""{"v":1e2147483648}""", c11);

        // Int64 boundary: normalization must not wrap the exponent.
        string json13 = """{"v": 10e9223372036854775807}""";
        string json14 = """{"v": 1e9223372036854775808}""";
        using var doc13 = System.Text.Json.JsonDocument.Parse(json13);
        using var doc14 = System.Text.Json.JsonDocument.Parse(json14);
        string c13 = CanonicalJson.SerializeCanonical(doc13.RootElement);
        string c14 = CanonicalJson.SerializeCanonical(doc14.RootElement);
        Assert.Equal(c14, c13);
        Assert.Equal("""{"v":1e9223372036854775808}""", c13);

        string json15 = """{"v": 0.1e-9223372036854775808}""";
        string json16 = """{"v": 1e-9223372036854775809}""";
        using var doc15 = System.Text.Json.JsonDocument.Parse(json15);
        using var doc16 = System.Text.Json.JsonDocument.Parse(json16);
        string c15 = CanonicalJson.SerializeCanonical(doc15.RootElement);
        string c16 = CanonicalJson.SerializeCanonical(doc16.RootElement);
        Assert.Equal(c16, c15);
        Assert.Equal("""{"v":1e-9223372036854775809}""", c15);

        // Also via RawJsonProcessor cooked artifact
        var processor = new RawJsonProcessor();
        var meta = AssetMeta.Parse("""{"schemaVersion":1,"assetId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","declaredType":"raw-json","logicalName":"game/a"}""", "a.json.meta", new List<ContentDiagnostic>());
        var result1 = processor.Process(System.Text.Encoding.UTF8.GetBytes("""{"v": 0.001e400}"""), meta, "a.json", default);
        var result2 = processor.Process(System.Text.Encoding.UTF8.GetBytes("""{"v": 1e397}"""), meta, "a.json", default);
        Assert.Equal(System.Text.Encoding.UTF8.GetString(result1.CookedBytes), System.Text.Encoding.UTF8.GetString(result2.CookedBytes));
    }
}
