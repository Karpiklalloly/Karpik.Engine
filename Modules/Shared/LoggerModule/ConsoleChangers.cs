namespace Karpik.Engine.Shared.Log;

public readonly ref struct ForegroundConsoleColor : IDisposable
{
    private readonly ConsoleColor _originalColor;
    
    public ForegroundConsoleColor(ConsoleColor color)
    {
        _originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
    }

    public void Dispose()
    {
        Console.ForegroundColor = _originalColor;
    }
}

public readonly ref struct BackgroundConsoleColor : IDisposable
{
    private readonly ConsoleColor _originalColor;
    
    public BackgroundConsoleColor(ConsoleColor color)
    {
        _originalColor = Console.BackgroundColor;
        Console.BackgroundColor = color;
    }

    public void Dispose()
    {
        Console.BackgroundColor = _originalColor;
    }
}