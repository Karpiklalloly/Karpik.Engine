namespace Karpik.Engine.Packager;

public static class Program
{
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        string[] required = ["--source", "--output", "--engine-version", "--sdk-version"];
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !required.Contains(args[index], StringComparer.Ordinal) ||
                string.IsNullOrWhiteSpace(args[index + 1]) || !values.TryAdd(args[index], args[index + 1]))
            {
                WriteUsage(error);
                return 2;
            }
        }
        if (required.Any(argument => !values.ContainsKey(argument)))
        {
            WriteUsage(error);
            return 2;
        }

        try
        {
            EnginePayloadBuildResult result = new EnginePayloadBuilder().Build(
                values["--source"],
                values["--output"],
                values["--engine-version"],
                values["--sdk-version"]);
            output.WriteLine(result.ReusedExistingInstallation
                ? $"Reused engine payload: {result.DestinationDirectory}"
                : $"Published engine payload: {result.DestinationDirectory}");
            output.WriteLine($"Content SHA-256: {result.ContentHash}");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or DotNetProcessTerminationException)
        {
            error.WriteLine($"Engine payload packaging failed: {exception.Message}");
            return 1;
        }
    }

    private static void WriteUsage(TextWriter error) =>
        error.WriteLine("Usage: Karpik.Engine.Packager --source <path> --output <path> --engine-version <version> --sdk-version <version>");
}
