namespace Karpik.Editor;

public static class ConsoleMessageCopy
{
    public static async Task<bool> TryCopyAsync(
        object? selectedItem,
        Func<string, Task>? writeTextAsync)
    {
        if (selectedItem is not string message || writeTextAsync is null)
        {
            return false;
        }

        try
        {
            await writeTextAsync(message);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
