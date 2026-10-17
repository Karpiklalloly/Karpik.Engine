using Karpik.Engine.Tooling;
using Xunit;

namespace Karpik.Engine.Tooling.Tests;

public sealed class EngineInstallationValidationCacheTests
{
    [Fact]
    public void ResolverRecordsAReceiptAfterFullValidation()
    {
        using var temporary = new TemporaryDirectory();
        string installation = TestInstallation.Create(temporary.RootPath);
        var cache = new EngineInstallationValidationCache(Path.Combine(temporary.RootPath, "cache"));
        var resolver = new EngineInstallationResolver(
            localApplicationDataRoot: temporary.RootPath,
            validationCache: cache);

        EngineInstallationResolutionResult result = resolver.Resolve("0.6.0-sdk", installation);

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(cache.IsCurrent(installation, result.Manifest!));
    }

    [Fact]
    public void CachedValidationAcceptsUnchangedInstallationAndInvalidatesChangedPayload()
    {
        using var temporary = new TemporaryDirectory();
        string installation = TestInstallation.Create(temporary.RootPath);
        var cache = new EngineInstallationValidationCache(Path.Combine(temporary.RootPath, "cache"));
        EngineInstallationValidationResult validation = new EngineInstallationValidator().Validate(installation);

        cache.Record(installation, validation.Manifest!);

        Assert.True(cache.IsCurrent(installation, validation.Manifest!));

        File.AppendAllText(Path.Combine(installation, "modules", "Module", "Module.dll"), "changed");

        Assert.False(cache.IsCurrent(installation, validation.Manifest!));
    }
}
