namespace Karpik.Editor;

public sealed record EditorStartupOptions(string? SolutionPath, string? HandoffPath)
{
    public static EditorStartupOptions Parse(IReadOnlyList<string>? arguments)
    {
        string? solutionPath = null;
        string? handoffPath = null;
        if (arguments is null)
        {
            return new EditorStartupOptions(null, null);
        }

        for (int index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] == "--solution" && index + 1 < arguments.Count)
            {
                solutionPath = arguments[++index];
            }
            else if (arguments[index] == "--handoff" && index + 1 < arguments.Count)
            {
                handoffPath = arguments[++index];
            }
        }
        return new EditorStartupOptions(solutionPath, handoffPath);
    }
}
