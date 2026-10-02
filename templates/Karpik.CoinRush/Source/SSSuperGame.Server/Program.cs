using SSSuperGame.Shared;

namespace SSSuperGame.Server;

/// <summary>Provides the sample server entry point.</summary>
public static class Program
{
    /// <summary>Prints the server greeting.</summary>
    public static void Main() => Console.WriteLine($"{GameGreeting.Default.Text} server");
}
