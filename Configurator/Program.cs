namespace ProjectConfigurator;

internal static class Program
{
    private static int Main(string[] args)
    {
        var rootPath = FindSolutionRoot(Directory.GetCurrentDirectory());
        if (rootPath == null)
        {
            Console.Error.WriteLine("Root not found (no .slnx).");
            return 1;
        }

        var solutionPath = Directory.GetFiles(rootPath, "*.slnx").Single();
        var model = RepositoryParser.Load(rootPath, solutionPath);
        var graph = GraphValidator.Validate(model);
        if (!graph.IsValid)
        {
            PrintErrors(graph.Errors);
            return 1;
        }

        var artifacts = ArtifactGenerator.BuildArtifacts(model, graph);
        if (args.Contains("--validate"))
        {
            return ValidateArtifacts(rootPath, artifacts);
        }
        if (args.Contains("--generate") || args.Contains("-g"))
        {
            ArtifactGenerator.WriteArtifacts(artifacts);
            Console.WriteLine("Files generated successfully.");
            return 0;
        }

        return RunInteractive(model);
    }

    private static int RunInteractive(RepositoryModel model)
    {
        var selections = model.Selections.Values
            .OrderBy(selection => selection.ModuleId, StringComparer.Ordinal)
            .ToList();
        var selectedIndex = 0;
        while (true)
        {
            Console.Clear();
            Console.WriteLine("CONFIGURATOR");
            Console.WriteLine("[SPACE]: Toggle | [ENTER]: Switch Impl | [S]: Save | [Q]: Quit\n");
            for (var index = 0; index < selections.Count; index++)
            {
                var selection = selections[index];
                Console.Write(index == selectedIndex ? " > " : "   ");
                Console.Write($"{(selection.Enabled ? "[x]" : "[ ]")} {selection.ModuleId.PadRight(30)}");
                if (model.Modules[selection.ModuleId].Implementations.Count > 0)
                {
                    Console.Write($" < {selection.Implementation} >");
                }
                Console.WriteLine();
            }

            switch (Console.ReadKey(true).Key)
            {
                case ConsoleKey.UpArrow:
                    selectedIndex = Math.Max(0, selectedIndex - 1);
                    break;
                case ConsoleKey.DownArrow:
                    selectedIndex = Math.Min(selections.Count - 1, selectedIndex + 1);
                    break;
                case ConsoleKey.Spacebar:
                    selections[selectedIndex].Enabled = !selections[selectedIndex].Enabled;
                    break;
                case ConsoleKey.Enter:
                    SelectNextImplementation(model.Modules[selections[selectedIndex].ModuleId], selections[selectedIndex]);
                    break;
                case ConsoleKey.S:
                    RepositoryParser.SaveProfile(model, selections);
                    Console.WriteLine("\nProfile saved. Run Configurator --generate to update artifacts.");
                    Thread.Sleep(700);
                    break;
                case ConsoleKey.Q:
                    return 0;
            }
        }
    }

    private static void SelectNextImplementation(ModuleInfo module, ModuleSelection selection)
    {
        if (module.Implementations.Count == 0)
        {
            return;
        }
        var implementations = module.Implementations.Keys.ToList();
        var index = implementations.IndexOf(selection.Implementation ?? "");
        selection.Implementation = implementations[(index + 1) % implementations.Count];
    }

    private static int ValidateArtifacts(string rootPath, IReadOnlyDictionary<string, string> artifacts)
    {
        var stale = artifacts
            .Where(artifact => !File.Exists(artifact.Key) ||
                               !string.Equals(File.ReadAllText(artifact.Key), artifact.Value, StringComparison.Ordinal))
            .Select(artifact => Path.GetRelativePath(rootPath, artifact.Key))
            .ToList();
        if (stale.Count == 0)
        {
            Console.WriteLine("Module graph validation passed.");
            return 0;
        }

        Console.Error.WriteLine($"Generated artifacts are stale: {string.Join(", ", stale)}");
        Console.Error.WriteLine("Run: dotnet run --project Configurator\\Configurator.csproj -- --generate");
        return 1;
    }

    private static void PrintErrors(IEnumerable<string> errors)
    {
        Console.Error.WriteLine("Module graph validation failed:");
        foreach (var error in errors)
        {
            Console.Error.WriteLine($"  - {error}");
        }
    }

    private static string? FindSolutionRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
        {
            if (directory.GetFiles("*.slnx").Length > 0)
            {
                return directory.FullName;
            }
        }
        return null;
    }
}
