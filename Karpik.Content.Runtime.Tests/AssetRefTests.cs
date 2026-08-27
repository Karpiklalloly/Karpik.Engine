using Karpik.Content.Runtime;
using Xunit;

public sealed class AssetRefTests
{
    [Fact]
    public void IsAlive_AfterRegister()
    {
        var id = new Karpik.Content.Core.AssetId(Guid.NewGuid());
        var r = new AssetRef<Karpik.Content.Runtime.RawJsonPayload>(id, 1);
        Assert.False(r.IsAlive(null!));
    }
}
