using Karpik.Editor;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class ConsoleMessageCopyTests
{
    [Fact]
    public async Task TryCopyAsync_SelectedMessage_WritesExactDisplayedText()
    {
        const string displayedMessage = "[12:34:56] [Server] ready  ";
        string? copied = null;

        bool result = await ConsoleMessageCopy.TryCopyAsync(
            displayedMessage,
            text =>
            {
                copied = text;
                return Task.CompletedTask;
            });

        Assert.True(result);
        Assert.Equal(displayedMessage, copied);
    }

    [Fact]
    public async Task TryCopyAsync_NoSelection_DoesNotWrite()
    {
        int writes = 0;

        bool result = await ConsoleMessageCopy.TryCopyAsync(
            null,
            _ =>
            {
                writes++;
                return Task.CompletedTask;
            });

        Assert.False(result);
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task TryCopyAsync_ClipboardFailure_IsNoOp()
    {
        bool result = await ConsoleMessageCopy.TryCopyAsync(
            "message",
            _ => throw new InvalidOperationException("Clipboard unavailable."));

        Assert.False(result);
    }
}
