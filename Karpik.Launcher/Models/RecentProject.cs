namespace Karpik.Launcher.Models;

public sealed record RecentProject(string SolutionPath, DateTimeOffset LastOpenedUtc);
