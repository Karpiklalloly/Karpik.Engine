using Karpik.Content.Core;
using Xunit;

namespace Karpik.Content.Tests;

public sealed class AssetTargetTests
{
    [Fact]
    public void Shared_CombinesClientAndServer()
    {
        Assert.Equal(AssetTarget.Client | AssetTarget.Server, AssetTarget.Shared);
        Assert.True(AssetTarget.Shared.HasFlag(AssetTarget.Client));
        Assert.True(AssetTarget.Shared.HasFlag(AssetTarget.Server));
    }

    [Theory]
    [InlineData("Client", AssetTarget.Client)]
    [InlineData("Server", AssetTarget.Server)]
    [InlineData("client", AssetTarget.Client)]
    [InlineData("server", AssetTarget.Server)]
    public void TryParse_KnownNames_Succeeds(string text, AssetTarget expected)
    {
        Assert.True(AssetTargets.TryParse(text, out AssetTarget actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Shared")]
    [InlineData("Both")]
    [InlineData("Client,Server")]
    public void TryParse_UnknownNames_Fails(string text)
    {
        Assert.False(AssetTargets.TryParse(text, out _));
    }

    [Theory]
    [InlineData(AssetTarget.Client, "Client")]
    [InlineData(AssetTarget.Server, "Server")]
    public void ToCanonicalString_SingleTarget_RoundTrips(AssetTarget target, string expected)
    {
        Assert.Equal(expected, target.ToCanonicalString());
        Assert.True(AssetTargets.TryParse(expected, out AssetTarget parsed));
        Assert.Equal(target, parsed);
    }
}
