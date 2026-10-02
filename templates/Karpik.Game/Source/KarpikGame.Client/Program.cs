using KarpikGame.Shared;

namespace KarpikGame.Client;

public static class ClientGreeting
{
    public static string Create() => $"{GameGreeting.Default.Text} client";
}

public static class Program
{
    public static void Main() => Console.WriteLine(ClientGreeting.Create());
}
