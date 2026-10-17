namespace Karpik.Editor;

public sealed record EditorConsoleLogEntry(DateTimeOffset Timestamp, string Session, int Level, string Message);
