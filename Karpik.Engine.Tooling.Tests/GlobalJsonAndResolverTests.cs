using Karpik.Engine.Tooling;
using Xunit;

namespace Karpik.Engine.Tooling.Tests;

public sealed class GlobalJsonAndResolverTests
{
    [Fact]
    public void GlobalJsonReaderReadsExactKarpikSdkVersion()
    {
        using var temporary = new TemporaryDirectory();
        string path = Path.Combine(temporary.RootPath, "global.json");
        File.WriteAllText(path, """
            {
              "sdk": { "version": "10.0.100" },
              "msbuild-sdks": { "Karpik.Engine.Sdk": "0.6.0-local" }
            }
            """);

        GlobalJsonSdkVersionResult result = new GlobalJsonSdkVersionReader().Read(path);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("0.6.0-local", result.SdkVersion);
    }

    [Theory]
    [InlineData("missing", GlobalJsonSdkVersionCode.MissingFile)]
    [InlineData("invalid", GlobalJsonSdkVersionCode.InvalidJson)]
    [InlineData("empty", GlobalJsonSdkVersionCode.MissingSdkVersion)]
    public void GlobalJsonReaderReturnsStableFailures(string variant, GlobalJsonSdkVersionCode expectedCode)
    {
        using var temporary = new TemporaryDirectory();
        string path = Path.Combine(temporary.RootPath, "global.json");
        if (variant == "invalid") File.WriteAllText(path, "{bad");
        if (variant == "empty") File.WriteAllText(path, "{}");

        GlobalJsonSdkVersionResult result = new GlobalJsonSdkVersionReader().Read(path);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, result.Code);
    }

    [Fact]
    public void ExplicitRootTakesPrecedenceOverDefaultStore()
    {
        using var temporary = new TemporaryDirectory();
        string explicitRoot = TestInstallation.Create(Path.Combine(temporary.RootPath, "explicit"), sdkVersion: "sdk-a");
        string defaultStore = Path.Combine(temporary.RootPath, "local", "Karpik", "Engines");
        TestInstallation.Create(defaultStore, directoryName: "default", engineVersion: "9.0.0", sdkVersion: "sdk-a");
        var resolver = new EngineInstallationResolver(localApplicationDataRoot: Path.Combine(temporary.RootPath, "local"));

        EngineInstallationResolutionResult result = resolver.Resolve("sdk-a", explicitRoot);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(Path.GetFullPath(explicitRoot), result.InstallationRoot);
        Assert.Equal("0.6.0", result.Manifest!.EngineVersion);
    }

    [Fact]
    public void ResolverScansDefaultLocalApplicationDataStoreForExactSdkVersion()
    {
        using var temporary = new TemporaryDirectory();
        string local = Path.Combine(temporary.RootPath, "local");
        string store = Path.Combine(local, "Karpik", "Engines");
        TestInstallation.Create(store, directoryName: "one", sdkVersion: "sdk-one");
        string expected = TestInstallation.Create(store, directoryName: "two", engineVersion: "0.7.0", sdkVersion: "sdk-two");

        EngineInstallationResolutionResult result = new EngineInstallationResolver(localApplicationDataRoot: local)
            .Resolve("sdk-two");

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(Path.GetFullPath(expected), result.InstallationRoot);
    }

    [Fact]
    public void ResolverRejectsTraversalAndAmbiguousExactMatches()
    {
        using var temporary = new TemporaryDirectory();
        string local = Path.Combine(temporary.RootPath, "local");
        string store = Path.Combine(local, "Karpik", "Engines");
        TestInstallation.Create(store, directoryName: "z-content-hash", sdkVersion: "same");
        TestInstallation.Create(store, directoryName: "a-content-hash", engineVersion: "0.7.0", sdkVersion: "same");
        var resolver = new EngineInstallationResolver(localApplicationDataRoot: local);

        Assert.Equal(EngineInstallationResolutionCode.InvalidSdkVersion, resolver.Resolve("../same").Code);
        EngineInstallationResolutionResult ambiguous = resolver.Resolve("same");
        Assert.Equal(EngineInstallationResolutionCode.AmbiguousInstallation, ambiguous.Code);
        Assert.Contains("a-content-hash, z-content-hash", ambiguous.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolverListsOnlyValidInstallationsNewestFirst()
    {
        using var temporary = new TemporaryDirectory();
        string local = Path.Combine(temporary.RootPath, "local");
        string store = Path.Combine(local, "Karpik", "Engines");
        TestInstallation.Create(store, directoryName: "old", sdkVersion: "0.6.0-local");
        TestInstallation.Create(store, directoryName: "new", sdkVersion: "0.7.0");
        string invalid = TestInstallation.Create(store, directoryName: "invalid", sdkVersion: "0.8.0");
        File.Delete(Path.Combine(invalid, ".complete"));

        IReadOnlyList<InstalledEngineInstallation> installations = new EngineInstallationResolver(localApplicationDataRoot: local)
            .ListInstalled();

        Assert.Equal(["0.7.0", "0.6.0-local"], installations.Select(item => item.Manifest.MsBuildSdkVersion));
    }
}
