namespace Karpik.Engine.MyGame.Client.Main;

public sealed class ClientReconnectTokenStore
{
    private readonly string _path;

    public ClientReconnectTokenStore()
    {
        var sessionId = AppContext.GetData("Karpik.HotReload.PipeName") as string;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            sessionId = "standalone";
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            sessionId = sessionId.Replace(invalidChar, '_');
        }

        _path = Path.Combine(AppContext.BaseDirectory, "reload", "client-session", $"{sessionId}.token");
    }

    public long Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return 0;
            }

            var text = File.ReadAllText(_path).Trim();
            return long.TryParse(text, out var token) && token > 0
                ? token
                : 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ClientReconnectTokenStore] Failed to read reconnect token: {ex.Message}");
            return 0;
        }
    }

    public void Save(long token)
    {
        if (token <= 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, token.ToString());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ClientReconnectTokenStore] Failed to write reconnect token: {ex.Message}");
        }
    }
}
