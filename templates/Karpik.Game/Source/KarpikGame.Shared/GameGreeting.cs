namespace KarpikGame.Shared;

public readonly record struct GameGreeting(string Text)
{
    public static GameGreeting Default => new("Hello from KarpikGame");
}
