using Karpik.Editor;
using ReactiveUI.Builder;
using Xunit;

namespace Karpik.Editor.Tests.Projects;

public sealed class EditorHandoffShellTests
{
    static EditorHandoffShellTests()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
    }

    [Fact]
    public async Task OpenProjectAsyncPropagatesHandoffWithoutCallingTheProjectOpener()
    {
        string root = Path.Combine(Path.GetTempPath(), "karpik-editor-shell-handoff", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string solution = Path.Combine(root, "Game.slnx");
            File.WriteAllText(solution, "<Solution />");
            var opener = new RecordingOpenService();
            using var shell = new EditorShellViewModel(
                new WorkspaceStore(Path.Combine(root, "workspace.json")),
                opener,
                new RequestedHandoffService());

            ProjectOpenResult result = await shell.OpenProjectAsync(
                solution,
                TestContext.Current.CancellationToken);

            Assert.True(result.RequiresEditorHandoff);
            Assert.Equal(0, opener.OpenCount);
            await shell.ShutdownAsync();
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class RecordingOpenService : IProjectOpenService
    {
        public int OpenCount { get; private set; }

        public Task<ProjectOpenResult> OpenAsync(
            string solutionPath,
            ProjectGeneration generation,
            CancellationToken cancellationToken)
        {
            OpenCount++;
            return Task.FromResult(ProjectOpenResult.Failure("must not open"));
        }
    }

    private sealed class RequestedHandoffService : IProjectHandoffService
    {
        public ProjectHandoffResult Prepare(string solutionPath) =>
            ProjectHandoffResult.Requested("different SDK");
    }
}
