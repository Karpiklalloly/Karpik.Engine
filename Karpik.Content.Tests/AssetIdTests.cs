using Karpik.Content.Core;
using Xunit;

namespace Karpik.Content.Tests;

public sealed class AssetIdTests
{
    [Fact]
    public void ToCanonicalString_IsLowerCaseD()
    {
        var guid = Guid.Parse("01234567-89AB-CDEF-0123-456789ABCDEF");
        var id = new AssetId(guid);
        Assert.Equal("01234567-89ab-cdef-0123-456789abcdef", id.ToCanonicalString());
        Assert.Equal("01234567-89ab-cdef-0123-456789abcdef", id.ToString());
    }

    [Fact]
    public void TryParse_NormalizesCase()
    {
        string upper = "01234567-89AB-CDEF-0123-456789ABCDEF";
        Assert.True(AssetId.TryParse(upper, out AssetId id));
        Assert.Equal("01234567-89ab-cdef-0123-456789abcdef", id.ToCanonicalString());
    }

    [Fact]
    public void Parse_InvalidThrows()
    {
        Assert.Throws<FormatException>(() => AssetId.Parse("not-a-guid"));
    }

    [Fact]
    public void EqualityAndOrdering()
    {
        var a = new AssetId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var b = new AssetId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
        Assert.Equal(a, new AssetId(Guid.Parse("11111111-1111-1111-1111-111111111111")));
    }
}
