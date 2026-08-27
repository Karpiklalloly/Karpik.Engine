using Karpik.Content.Runtime;
using Xunit;

internal sealed class RawJsonPayload
{
}

public sealed class AssetRefTests
{
    [Fact]
    public void IsAlive_AfterRegister()
    {
        var id = new Karpik.Content.Core.AssetId(Guid.NewGuid());
        var r = new AssetRef<RawJsonPayload>(id, "game/a", 1);
        Assert.False(r.IsAlive(null!));
    }
}
