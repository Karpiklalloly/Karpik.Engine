using Karpik.Engine.Tooling;
using Xunit;

namespace Karpik.Engine.Tooling.Tests;

public sealed class EditorHandoffRequestTests
{
    [Fact]
    public void RoundTripPreservesAnAbsoluteSolutionPathAndProtocol()
    {
        using var temporary = new TemporaryDirectory();
        string solutionPath = Path.Combine(temporary.RootPath, "Game.slnx");
        File.WriteAllText(solutionPath, "<Solution />");
        var request = new EditorHandoffRequest(solutionPath);

        EditorHandoffRequest parsed = EditorHandoffRequest.Parse(request.ToJson());

        Assert.Equal(EditorHandoffRequest.CurrentProtocolVersion, parsed.ProtocolVersion);
        Assert.Equal(Path.GetFullPath(solutionPath), parsed.SolutionPath);
        Assert.Equal(20, EditorExitCodes.HandoffRequested);
    }

    [Fact]
    public void ParseRejectsUnknownFieldsAndUnsafeSolutionPaths()
    {
        Assert.Throws<InvalidDataException>(() => EditorHandoffRequest.Parse(
            """{"protocolVersion":1,"solutionPath":"Game.slnx","extra":true}"""));
        Assert.Throws<InvalidDataException>(() => EditorHandoffRequest.Parse(
            """{"protocolVersion":1,"solutionPath":"Game.slnx"}"""));
    }

    [Fact]
    public void WriteCreatesAOneShotRequestThatCannotBeOverwritten()
    {
        using var temporary = new TemporaryDirectory();
        string solutionPath = Path.Combine(temporary.RootPath, "Game.slnx");
        string handoffPath = Path.Combine(temporary.RootPath, "handoff.json");
        File.WriteAllText(solutionPath, "<Solution />");
        var request = new EditorHandoffRequest(solutionPath);

        request.Write(handoffPath);

        Assert.Equal(Path.GetFullPath(solutionPath), EditorHandoffRequest.Read(handoffPath).SolutionPath);
        Assert.Throws<IOException>(() => request.Write(handoffPath));
    }
}
