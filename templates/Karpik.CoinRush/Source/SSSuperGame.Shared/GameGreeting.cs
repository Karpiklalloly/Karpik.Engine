namespace SSSuperGame.Shared;

/// <summary>Provides the shared greeting text for both game executables.</summary>
/// <param name="Text">The greeting shown by the executable.</param>
public readonly record struct GameGreeting(string Text)
{
    /// <summary>Gets the default game greeting.</summary>
    public static GameGreeting Default => new("Hello from SSSuperGame");
}
