using Karpik.Editor;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class EditorStartupOptionsTests
{
    [Fact]
    public void Parse_SolutionTakesPrecedenceAndMissingValuesAreIgnored()
    {
        EditorStartupOptions options = EditorStartupOptions.Parse(
            ["--handoff", "handoff.json", "--solution", "Game.slnx", "--handoff"]);

        Assert.Equal("Game.slnx", options.SolutionPath);
        Assert.Equal("handoff.json", options.HandoffPath);
    }

    [Fact]
    public void Parse_NullArgumentsReturnsEmptyOptions()
    {
        EditorStartupOptions options = EditorStartupOptions.Parse(null);

        Assert.Null(options.SolutionPath);
        Assert.Null(options.HandoffPath);
    }
}
