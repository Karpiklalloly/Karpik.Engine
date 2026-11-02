using Karpik.Content.Transport;
using Xunit;

namespace Karpik.Content.Transport.Tests;

public sealed class AddressTests
{
    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("../escape")]
    [InlineData("control\n")]
    public void RejectsInvalidInstance(string instance) => Assert.Throws<ArgumentException>(() =>
        ContentReloadAddress.Create(Path.GetTempPath(), instance, "game", ContentReloadTarget.Client));

    [Fact]
    public void CanonicalPathHasSameAddress()
    {
        string root = Path.Combine(Path.GetTempPath(), "game-content");
        var a = ContentReloadAddress.Create(root, "local-1", "game", ContentReloadTarget.Client);
        var b = ContentReloadAddress.Create(Path.Combine(root, "."), "local-1", "game", ContentReloadTarget.Client);
        var c = ContentReloadAddress.Create(root, "local-2", "game", ContentReloadTarget.Client);
        Assert.Equal(a.PipeName, b.PipeName);
        Assert.NotEqual(a.PipeName, c.PipeName);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, a.PipeName);
    }

    [Fact]
    public void RejectsInvalidIdentityAndBounds()
    {
        Assert.Throws<ArgumentException>(() => ContentReloadAddress.Create(Path.GetTempPath(), new string('x', 65), "game", ContentReloadTarget.Client));
        Assert.Throws<ArgumentException>(() => ContentReloadAddress.Create(Path.GetTempPath(), "x", "", ContentReloadTarget.Client));
        Assert.Throws<ArgumentException>(() => ContentReloadAddress.Create(Path.GetTempPath(), "x", "game", (ContentReloadTarget)99));
    }
}
