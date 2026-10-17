using System.Text.Json;
using System.Threading.Channels;

namespace Karpik.Editor;

public sealed class EditorLogArchive : IAsyncDisposable
{
    private const long DefaultMaximumBytes = 1L << 30;
    private readonly string _directory;
    private readonly long _maximumBytes;
    private readonly Channel<EditorConsoleLogEntry> _entries = Channel.CreateBounded<EditorConsoleLogEntry>(
        new BoundedChannelOptions(2_000) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private readonly Task _writer;

    public EditorLogArchive(string? directory = null, long maximumBytes = DefaultMaximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KarpikEngine", "Editor", "logs");
        _maximumBytes = maximumBytes;
        _writer = Task.Run(WriteAsync);
    }

    public void Append(EditorConsoleLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Writer.WriteAsync(entry).AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        _entries.Writer.TryComplete();
        await _writer;
    }

    private async Task WriteAsync()
    {
        Directory.CreateDirectory(_directory);
        Prune();
        string path = Path.Combine(_directory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
        await using var writer = new StreamWriter(stream);
        await foreach (EditorConsoleLogEntry entry in _entries.Reader.ReadAllAsync())
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(entry));
        }

        await writer.FlushAsync();
        Prune(path);
    }

    private void Prune(string? activePath = null)
    {
        var files = new DirectoryInfo(_directory).EnumerateFiles("*.jsonl")
            .Where(file => !string.Equals(file.FullName, activePath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToList();
        long total = files.Sum(file => file.Length);
        foreach (FileInfo file in files)
        {
            if (total <= _maximumBytes)
            {
                break;
            }

            total -= file.Length;
            file.Delete();
        }
    }
}
