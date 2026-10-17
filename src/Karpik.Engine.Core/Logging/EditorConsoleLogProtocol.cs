using System.Text.Json;

namespace Karpik.Engine.Core;

public sealed record EditorConsoleLogEvent(DateTimeOffset Timestamp, int Level, string Message);

public static class EditorConsoleLogProtocol
{
    public const string Prefix = "@karpik-editor-log:";

    public static string Serialize(DateTimeOffset timestamp, int level, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Prefix + JsonSerializer.Serialize(new EditorConsoleLogEvent(timestamp, level, message));
    }

    public static bool TryParse(string line, out EditorConsoleLogEvent? entry)
    {
        ArgumentNullException.ThrowIfNull(line);
        entry = null;
        if (!line.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            entry = JsonSerializer.Deserialize<EditorConsoleLogEvent>(line.AsSpan(Prefix.Length));
            return entry?.Message is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
