using KarpikGame.Client;
using Xunit;

namespace KarpikGame.Tests;

public sealed class GreetingTests
{
    [Fact]
    public void Client_greeting_uses_the_shared_game_value()
    {
        Assert.Equal("Hello from KarpikGame client", ClientGreeting.Create());
    }
}
