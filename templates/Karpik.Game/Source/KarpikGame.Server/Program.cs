using KarpikGame.Shared;

namespace KarpikGame.Server;

public static class Program
{
    public static void Main() => Console.WriteLine($"{GameGreeting.Default.Text} server");
}
