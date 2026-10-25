namespace ProjectConfigurator;

/// <summary>Build-time ownership and propagation over the existing project graph.</summary>
public static class ModuleCodegen
{
    public static void Discover(IEnumerable<ProjectInfo> projects, List<string> errors)
    {
        var generators = projects.Where(project => project.IsCodegen)
            .GroupBy(project => project.PluginId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var owner in projects.Where(project => !project.IsCodegen))
        {
            if (!generators.TryGetValue(owner.PluginId + ".Codegen", out var matches)) continue;
            if (matches.Length != 1)
            {
                errors.Add($"Ambiguous codegen for {owner.RelativePath}: {string.Join(", ", matches.Select(match => match.RelativePath))}.");
                continue;
            }
            owner.CodegenProject = matches[0];
        }
    }

    private static ProjectInfo[] Dependencies(RepositoryModel model, ProjectInfo project, HashSet<string> selected)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { project.AbsolutePath };
        var result = new List<ProjectInfo>();
        Visit(project);
        return result.ToArray();

        void Visit(ProjectInfo current)
        {
            var references = current.DirectProjectReferences.Where(path => !current.AnalyzerProjectReferences.Contains(path))
                .Concat(ModuleDependencyPaths(current, selected));
            foreach (var path in references)
            {
                if (!visited.Add(path) || !model.ProjectsByPath.TryGetValue(path, out var target) || target.IsCodegen) continue;
                result.Add(target);
                Visit(target);
            }
        }
    }

    public static ProjectInfo[] GeneratorsFor(RepositoryModel model, ProjectInfo consumer, HashSet<string> selected)
    {
        if (consumer.IsCodegen) return [];
        var modulePaths = model.Plugins.Select(plugin => plugin.Project.AbsolutePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Dependencies(model, consumer, selected)
            .Where(owner => owner.CodegenProject != null && (!modulePaths.Contains(owner.AbsolutePath) || selected.Contains(owner.AbsolutePath)))
            .Select(owner => owner.CodegenProject!)
            .DistinctBy(generator => generator.AbsolutePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(generator => generator.RelativePath, StringComparer.Ordinal).ToArray();
    }

    public static void ValidateCycles(RepositoryModel model, HashSet<string> selected, List<string> errors)
    {
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<ProjectInfo>();
        foreach (var project in model.ProjectsByPath.Values.OrderBy(project => project.RelativePath, StringComparer.Ordinal))
            if (!state.ContainsKey(project.AbsolutePath)) Visit(project);

        void Visit(ProjectInfo project)
        {
            state[project.AbsolutePath] = 1;
            stack.Add(project);
            var edges = project.DirectProjectReferences.Concat(ModuleDependencyPaths(project, selected))
                .Concat(GeneratorsFor(model, project, selected).Select(generator => generator.AbsolutePath));
            foreach (string edge in edges.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!model.ProjectsByPath.TryGetValue(edge, out var target)) continue;
                if (!state.TryGetValue(edge, out int targetState)) Visit(target);
                else if (targetState == 1)
                {
                    var cycle = stack.SkipWhile(item => item != target).Append(target).ToArray();
                    if (cycle.Any(item => item.IsCodegen))
                        errors.Add($"Codegen build cycle: {string.Join(" -> ", cycle.Select(item => item.RelativePath))}.");
                }
            }
            stack.RemoveAt(stack.Count - 1);
            state[project.AbsolutePath] = 2;
        }
    }

    private static IEnumerable<string> ModuleDependencyPaths(ProjectInfo project, HashSet<string> selected) =>
        project.Dependencies.Where(dependency => dependency.TargetPath != null &&
            (!dependency.Optional || selected.Contains(dependency.TargetPath))).Select(dependency => dependency.TargetPath!);
}
