using Karpik.Engine.Shared.Modding.Lua;
using Newtonsoft.Json.Linq;
using Xunit;

public sealed class ModDependencyManifestTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("""{"shared":[],"client":[],"server":[]}""")]
    public void OmittedAndEmptyGroupsHaveNoDeclarations(string? json)
    {
        var manifest = ModDependencyManifest.Parse("source", json is null ? null : JToken.Parse(json));

        Assert.Empty(manifest.Shared);
        Assert.Empty(manifest.Client);
        Assert.Empty(manifest.Server);
    }

    [Fact]
    public void PreservesExactIdsAndOptionalFlagsInEachScope()
    {
        var token = JObject.Parse("""
            {
              "shared": [{"id":"Provider"}, {"id":"provider", "optional":false}],
              "client": [{"id":"Provider", "optional":true}],
              "server": [{"id":" Provider "}]
            }
            """);
        var manifest = ModDependencyManifest.Parse("source", token);
        token.RemoveAll(); // Published declarations never depend on mutable JSON tokens.

        Assert.Collection(manifest.Shared,
            dependency => Assert.Equal(("Provider", false), (dependency.Id, dependency.Optional)),
            dependency => Assert.Equal(("provider", false), (dependency.Id, dependency.Optional)));
        var client = Assert.Single(manifest.Client);
        Assert.Equal(("Provider", true), (client.Id, client.Optional));
        Assert.Equal(" Provider ", Assert.Single(manifest.Server).Id);
    }

    [Fact]
    public void ParsesLongSourceIdWithoutAllocatingDiagnosticTextForEveryDeclaration()
    {
        string source = new('s', 4096);
        var entries = new JArray(Enumerable.Range(0, 768).Select(i => new JObject { ["id"] = $"p{i}", ["optional"] = true }));
        var token = new JObject { ["shared"] = entries };
        _ = ModDependencyManifest.Parse("warmup", token);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var manifest = ModDependencyManifest.Parse(source, token);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(entries.Count, manifest.Shared.Length);
        Assert.True(allocated < 4 * 1024 * 1024, $"Parsing allocated {allocated:N0} bytes.");
    }

    [Theory]
    [InlineData("null", "dependencies")]
    [InlineData("[]", "dependencies")]
    [InlineData("""{"other":[]}""", "other")]
    [InlineData("""{"Client":[]}""", "Client")]
    [InlineData("""{"client":null}""", "client")]
    [InlineData("""{"server":{}}""", "server")]
    [InlineData("""{"server":[null]}""", "server")]
    [InlineData("""{"shared":["target"]}""", "shared")]
    [InlineData("""{"shared":[{}]}""", "id")]
    [InlineData("""{"client":[{"id":4}]}""", "id")]
    [InlineData("""{"client":[{"id":null}]}""", "id")]
    [InlineData("""{"client":[{"id":"  "}]}""", "id")]
    [InlineData("""{"server":[{"id":"target","optional":"false"}]}""", "optional")]
    [InlineData("""{"server":[{"id":"target","optional":null}]}""", "optional")]
    [InlineData("""{"server":[{"id":"target","optional":1}]}""", "optional")]
    [InlineData("""{"server":[{"id":"target","unexpected":true}]}""", "unexpected")]
    [InlineData("""{"server":[{"id":"target","Optional":true}]}""", "Optional")]
    public void RejectsInvalidTokenShapesAndNamesInEveryScope(string json, string context)
    {
        var error = Assert.Throws<ModDependencyException>(() => ModDependencyManifest.Parse("source", JToken.Parse(json)));

        Assert.Contains("'source'", error.Message);
        Assert.Contains(context, error.Message);
        if (json.Contains("\"id\":\"target\"", StringComparison.Ordinal))
        {
            Assert.Contains("server", error.Message);
            Assert.Contains("'target'", error.Message);
        }
    }

    [Fact]
    public void ReadsNumericBoundsAndLeavesOmittedBoundsUnset()
    {
        var manifest = ModDependencyManifest.Parse("source", JObject.Parse("""
            {
              "shared": [{"id":"provider", "minVersion":"000.00.0", "maxVersionExclusive":"2147483647.2147483647.2147483647"}],
              "client": [{"id":"provider", "minVersion":"1.10.0"}],
              "server": [{"id":"provider", "maxVersionExclusive":"2.0.0"}]
            }
            """));

        var shared = Assert.Single(manifest.Shared);
        Assert.Equal(new Version(0, 0, 0), shared.MinVersion);
        Assert.Equal(new Version(int.MaxValue, int.MaxValue, int.MaxValue), shared.MaxVersionExclusive);
        var client = Assert.Single(manifest.Client);
        Assert.Equal(new Version(1, 10, 0), client.MinVersion);
        Assert.Null(client.MaxVersionExclusive);
        var server = Assert.Single(manifest.Server);
        Assert.Null(server.MinVersion);
        Assert.Equal(new Version(2, 0, 0), server.MaxVersionExclusive);
    }

    [Theory]
    [InlineData("minVersion", "null")]
    [InlineData("maxVersionExclusive", "1.0")]
    [InlineData("minVersion", "\"\"")]
    [InlineData("minVersion", "\"1.0\"")]
    [InlineData("minVersion", "\"1.0.0.0\"")]
    [InlineData("minVersion", "\"1..0\"")]
    [InlineData("minVersion", "\"-1.0.0\"")]
    [InlineData("minVersion", "\"+1.0.0\"")]
    [InlineData("minVersion", "\"1. 0.0\"")]
    [InlineData("minVersion", "\"1.0.0 \"")]
    [InlineData("minVersion", "\"1.0.0\\u0000\"")]
    [InlineData("minVersion", "\"１.0.0\"")]
    [InlineData("maxVersionExclusive", "\"0.2147483648.0\"")]
    [InlineData("maxVersionExclusive", "\"1.0.0-rc.1\"")]
    public void RejectsNonNumericOrMistypedBoundsInInactiveScopeToo(string field, string jsonBound)
    {
        var token = JObject.Parse($$"""{"server":[{"id":"provider", "{{field}}":{{jsonBound}}}]}""");

        var error = Assert.Throws<ModDependencyException>(() => ModDependencyManifest.Parse("source", token));

        Assert.Contains("Mod 'source' dependencies.server[0] target 'provider'", error.Message);
        Assert.Contains(field, error.Message);
        Assert.Contains("MAJOR.MINOR.PATCH", error.Message);
    }

    [Fact]
    public void RejectsExclusiveMaximumAtZeroWithoutExplicitMinimum()
    {
        var token = JObject.Parse("""{"client":[{"id":"provider", "maxVersionExclusive":"0.0.0"}]}""");

        var error = Assert.Throws<ModDependencyException>(() => ModDependencyManifest.Parse("source", token));

        Assert.Contains("Mod 'source' dependencies.client[0] target 'provider'", error.Message);
        Assert.Contains("maxVersionExclusive '0.0.0'", error.Message);
        Assert.Contains("empty", error.Message);
    }

    [Theory]
    [InlineData("2.0.0", "1.10.0")]
    [InlineData("1.01.0", "1.1.0")]
    public void RejectsReversedAndEmptyNumericIntervals(string minimum, string maximum)
    {
        var token = JObject.Parse($$"""{"client":[{"id":"provider", "minVersion":"{{minimum}}", "maxVersionExclusive":"{{maximum}}"}]}""");

        var error = Assert.Throws<ModDependencyException>(() => ModDependencyManifest.Parse("source", token));

        Assert.Contains("Mod 'source' dependencies.client[0] target 'provider'", error.Message);
        Assert.Contains("minVersion", error.Message);
        Assert.Contains("maxVersionExclusive", error.Message);
        Assert.Contains(minimum, error.Message);
        Assert.Contains(maximum, error.Message);
    }

    [Theory]
    [InlineData("""{"shared":[{"id":"target"},{"id":"target","optional":true}]}""", "shared", "'target'", "duplicate")]
    [InlineData("""{"server":[{"id":"source","optional":true}]}""", "server", "'source'", "itself")]
    public void RejectsDuplicateTargetsWithinOneScopeAndSelfDependency(string json, string scope, string target, string reason)
    {
        var error = Assert.Throws<ModDependencyException>(() => ModDependencyManifest.Parse("source", JToken.Parse(json)));

        Assert.Contains("'source'", error.Message);
        Assert.Contains(scope, error.Message);
        Assert.Contains(target, error.Message);
        Assert.Contains(reason, error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
