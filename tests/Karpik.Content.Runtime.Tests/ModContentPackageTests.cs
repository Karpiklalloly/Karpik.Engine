using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Xunit;

namespace Karpik.Content.Runtime.Tests;

public sealed class ModContentPackageTests
{
    [Fact]
    public void SyntheticPackageKeepsOriginalsAndFreezesRuleOrderWithoutTakingOwnership()
    {
        var manifest = new ContentManifest(1, []);
        var store = new FixtureStore();
        ModContentOverride first = new(null, AssetId.New(), AssetId.New());
        ModContentOverride second = new("dependency", AssetId.New(), AssetId.New());
        List<ModContentOverride> rules = [first, second];
        var package = new ModContentPackage("patch", manifest, store, rules, "fixture://patch");
        rules.Reverse();

        Assert.Equal("patch", package.ModId);
        Assert.Equal("fixture://patch", package.SourcePath);
        Assert.Same(manifest, package.Manifest);
        Assert.Same(store, package.Store);
        Assert.Equal([first, second], package.Overrides);
        Assert.Equal(0, store.Claims);
        Assert.Throws<NotSupportedException>(() => ((IList<ModContentOverride>)package.Overrides)[0] = second);
    }

    [Fact]
    public void SyntheticPackageRejectsStoreWithoutLifetime()
    {
        Assert.Throws<ArgumentException>(() => new ModContentPackage("patch", new(1, []), new UnownedStore(), [], "fixture"));
    }

    private class UnownedStore : IContentStore
    {
        public ReadOnlyMemory<byte> Get(string locator) => throw new NotSupportedException();
        public Task<ReadOnlyMemory<byte>> GetAsync(string locator, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FixtureStore : UnownedStore, IContentStoreLifetime
    {
        public int Claims { get; private set; }
        public void Retain() => Claims++;
        public void Release() => Claims--;
    }
}
