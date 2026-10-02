using SSSuperGame.Client;
using Xunit;

namespace SSSuperGame.Tests;

/// <summary>Checks the client greeting exposed by the game assembly.</summary>
public sealed class GreetingTests
{
    /// <summary>Verifies that client greeting uses the shared game value.</summary>
    [Fact]
    public void Client_greeting_uses_the_shared_game_value()
    {
        Assert.Equal("Hello from SSSuperGame client", ClientGreeting.Create());
    }
}
