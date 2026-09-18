using Karpik.Editor;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class EditorConsoleHistoryTests
{
    [Fact]
    public void Filters_HideEntriesWithoutRemovingThemFromHistory()
    {
        var console = new ConsoleViewModel();
        console.RegisterSession("Сервер");
        console.RegisterSession("Клиент 1");
        console.Add(new EditorConsoleLogEntry(DateTimeOffset.UtcNow, "Сервер", Level: 1, "debug"));
        console.Add(new EditorConsoleLogEntry(DateTimeOffset.UtcNow, "Клиент 1", Level: 3, "warning"));

        console.MinimumLevel = 3;
        console.SelectedSession = "Сервер";

        Assert.Equal(2, console.AllEntries.Count);
        Assert.Empty(console.Entries);
        Assert.Contains("Клиент 1", console.Sessions);
    }

    [Fact]
    public async Task Archive_WritesOneJsonLinePerEntry()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"KarpikEditorLogs-{Guid.NewGuid():N}");
        try
        {
            await using (var archive = new EditorLogArchive(directory, maximumBytes: 1_024))
            {
                archive.Append(new EditorConsoleLogEntry(
                    DateTimeOffset.UtcNow,
                    "Сервер",
                    Level: 2,
                    "first\r\nsecond"));
            }

            string file = Assert.Single(Directory.GetFiles(directory, "*.jsonl"));
            string line = Assert.Single(await File.ReadAllLinesAsync(file, TestContext.Current.CancellationToken));
            Assert.DoesNotContain('\r', line);
            Assert.Contains("\\r\\n", line);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
