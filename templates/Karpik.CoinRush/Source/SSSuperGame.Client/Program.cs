using SSSuperGame.Shared;

namespace SSSuperGame.Client;

/// <summary>Formats the client greeting shown by the sample entry point.</summary>
public static class ClientGreeting
{
    /// <summary>Creates the client greeting.</summary>
    /// <returns>The shared greeting with the client role appended.</returns>
    public static string Create() => $"{GameGreeting.Default.Text} client";
}

/// <summary>Provides the sample client entry point.</summary>
public static class Program
{
    /// <summary>Prints the client greeting.</summary>
    public static void Main() => Console.WriteLine(ClientGreeting.Create());
}
