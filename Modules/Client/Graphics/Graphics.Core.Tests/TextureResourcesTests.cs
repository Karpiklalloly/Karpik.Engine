using Karpik.Engine.Client.Graphics.Core.Sets;
using Xunit;

namespace Karpik.Engine.Client.Graphics.Core.Tests;

public class TextureResourcesTests
{
    [Fact]
    public void Dispose_before_init_is_safe()
    {
        var resources = new TextureResources();
        var exception = Record.Exception(() => resources.Dispose());
        Assert.Null(exception);
    }
}
