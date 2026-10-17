using Karpik.Engine.Core;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class EditorConsoleLogProtocolTests
{
    [Fact]
    public void Serialize_RoundTripsMultilineMessageAsOnePhysicalLine()
    {
        DateTimeOffset timestamp = new(2026, 9, 18, 12, 34, 56, TimeSpan.Zero);

        string line = EditorConsoleLogProtocol.Serialize(timestamp, level: 2, "first\r\nsecond");

        Assert.StartsWith(EditorConsoleLogProtocol.Prefix, line);
        Assert.DoesNotContain('\r', line);
        Assert.DoesNotContain('\n', line);
        Assert.True(EditorConsoleLogProtocol.TryParse(line, out EditorConsoleLogEvent? entry));
        Assert.Equal(timestamp, entry!.Timestamp);
        Assert.Equal(2, entry.Level);
        Assert.Equal("first\r\nsecond", entry.Message);
    }

    [Fact]
    public void TryParse_RejectsMalformedMarkedJson()
    {
        bool parsed = EditorConsoleLogProtocol.TryParse(
            EditorConsoleLogProtocol.Prefix + "{not-json}",
            out EditorConsoleLogEvent? entry);

        Assert.False(parsed);
        Assert.Null(entry);
    }
}
