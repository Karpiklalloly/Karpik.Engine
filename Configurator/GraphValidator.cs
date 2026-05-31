using System.Globalization;

namespace ProjectConfigurator;

public static class GraphValidator
{
    public static GraphResult Validate(RepositoryModel model)
    {
        var result = new GraphResult();
        result.Errors.AddRange(model.ParseErrors);
        ValidateConventions(model, result.Errors);
        ValidateSelections(model, result.Errors);
        ValidateSettings(model, result.Errors);
        if (result.Errors.Count > 0)
        {
            return result;
        }

        var activeProjects = SelectActiveProjects(model, result.ActivePlugins);
        ValidateDependencies(model, activeProjects, result.Errors);
        if (result.Errors.Count > 0)
        {
            return result;
        }

        result.ClientLoadOrder.AddRange(TopologicalSort(model, activeProjects, ProjectSide.Client, result.Errors));
        result.ServerLoadOrder.AddRange(TopologicalSort(model, activeProjects, ProjectSide.Server, result.Errors));
        return result;
    }

    private static void ValidateConventions(RepositoryModel model, List<string> errors)
    {
        foreach (var plugin in model.Plugins)
        {
            var project = plugin.Project;
            var directoryName = Path.GetFileName(Path.GetDirectoryName(project.AbsolutePath));
            if (!string.Equals(directoryName, project.PluginId, StringComparison.Ordinal))
            {
                errors.Add($"{project.RelativePath}: containing directory must match plugin id {project.PluginId}.");
            }
            if (project.AssemblyName != null && !string.Equals(project.AssemblyName, project.PluginId, StringComparison.Ordinal))
            {
                errors.Add($"{project.RelativePath}: AssemblyName must match plugin id {project.PluginId}.");
            }
        }

        foreach (var project in model.ProjectsByPath.Values.Where(project =>
                     project.RelativePath.StartsWith("Modules/", StringComparison.OrdinalIgnoreCase) ||
                     project.RelativePath.StartsWith("MyGame/", StringComparison.OrdinalIgnoreCase)))
        {
            if (project.DirectProjectReferences.Count > 0)
            {
                errors.Add($"{project.RelativePath}: direct ProjectReference is forbidden; use KarpikModuleDependency.");
            }
        }
    }

    private static void ValidateSelections(RepositoryModel model, List<string> errors)
    {
        foreach (var module in model.Modules.Values)
        {
            if (!model.Selections.TryGetValue(module.Id, out var selection))
            {
                errors.Add($"Missing KarpikModuleSelection for module {module.Id}.");
                continue;
            }

            if (module.Implementations.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(selection.Implementation))
                {
                    errors.Add($"Module {module.Id} has no implementations but profile selects {selection.Implementation}.");
                }
            }
            else if (selection.Enabled &&
                     (string.IsNullOrWhiteSpace(selection.Implementation) ||
                      !module.Implementations.ContainsKey(selection.Implementation)))
            {
                errors.Add($"Module {module.Id} must select one of: {string.Join(", ", module.Implementations.Keys)}.");
            }
        }

        foreach (var selection in model.Selections.Values)
        {
            if (!model.Modules.ContainsKey(selection.ModuleId))
            {
                errors.Add($"Profile selects unknown module {selection.ModuleId}.");
            }
        }
    }

    private static void ValidateSettings(RepositoryModel model, List<string> errors)
    {
        var schemas = model.Plugins
            .SelectMany(plugin => plugin.Project.Settings.Select(setting => setting with { ModuleId = plugin.ModuleId }))
            .GroupBy(setting => $"{setting.ModuleId}.{setting.Name}", StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var pair in schemas)
        {
            var schema = pair.Value;
            var hasValue = model.SettingValues.TryGetValue(pair.Key, out var value);
            value ??= schema.DefaultValue;
            if (schema.Required && string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"Required module setting is missing: {pair.Key}");
                continue;
            }
            if (value != null && !IsValidSetting(schema, value))
            {
                errors.Add($"Module setting {pair.Key} has invalid {schema.Type} value '{value}'.");
            }
        }

        foreach (var key in model.SettingValues.Keys)
        {
            if (!schemas.ContainsKey(key))
            {
                errors.Add($"Profile sets unknown module setting {key}.");
            }
        }
    }

    private static bool IsValidSetting(SettingSchema schema, string value)
    {
        return schema.Type switch
        {
            "String" => true,
            "Boolean" => bool.TryParse(value, out _),
            "Int32" => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            "Double" => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
            "Enum" => schema.AllowedValues.Contains(value, StringComparer.Ordinal),
            _ => false
        };
    }

    private static Dictionary<string, ProjectInfo> SelectActiveProjects(
        RepositoryModel model,
        List<PluginInfo> activePlugins)
    {
        var active = model.GameRoots.ToDictionary(project => project.AbsolutePath, StringComparer.OrdinalIgnoreCase);
        foreach (var module in model.Modules.Values)
        {
            var selection = model.Selections[module.Id];
            if (!selection.Enabled)
            {
                continue;
            }

            Add(module.Standalone);
            Add(module.Core);
            if (!string.IsNullOrWhiteSpace(selection.Implementation))
            {
                Add(module.Implementations[selection.Implementation]);
            }
        }
        return active;

        void Add(PluginInfo? plugin)
        {
            if (plugin == null)
            {
                return;
            }
            activePlugins.Add(plugin);
            active[plugin.Project.AbsolutePath] = plugin.Project;
        }
    }

    private static void ValidateDependencies(
        RepositoryModel model,
        IReadOnlyDictionary<string, ProjectInfo> activeProjects,
        List<string> errors)
    {
        var graphProjects = model.Plugins.Select(plugin => plugin.Project)
            .Concat(model.GameRoots)
            .DistinctBy(project => project.AbsolutePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var project in graphProjects)
        {
            foreach (var dependency in project.Dependencies)
            {
                if (dependency.TargetPath == null)
                {
                    continue;
                }

                if (!File.Exists(dependency.TargetPath))
                {
                    errors.Add($"{project.RelativePath}: dependency does not exist: {dependency.TargetPath}");
                    continue;
                }

                if (!model.ProjectsByPath.TryGetValue(dependency.TargetPath, out var target))
                {
                    continue;
                }

                if (!IsSideAllowed(project.Side, target.Side))
                {
                    errors.Add($"{project.RelativePath} ({project.Side}) references forbidden project {target.RelativePath} ({target.Side}).");
                }

                if (!activeProjects.ContainsKey(project.AbsolutePath))
                {
                    continue;
                }

                var isGraphProject = model.Plugins.Any(plugin =>
                                         string.Equals(plugin.Project.AbsolutePath, target.AbsolutePath, StringComparison.OrdinalIgnoreCase)) ||
                                     model.GameRoots.Any(root =>
                                         string.Equals(root.AbsolutePath, target.AbsolutePath, StringComparison.OrdinalIgnoreCase));
                if (isGraphProject && !activeProjects.ContainsKey(target.AbsolutePath) && !dependency.Optional)
                {
                    errors.Add($"{project.RelativePath}: required module dependency is disabled: {target.RelativePath}");
                }
            }
        }
    }

    private static IReadOnlyList<ProjectInfo> TopologicalSort(
        RepositoryModel model,
        IReadOnlyDictionary<string, ProjectInfo> activeProjects,
        ProjectSide side,
        List<string> errors)
    {
        var projects = activeProjects.Values
            .Where(project => project.Side == ProjectSide.Shared || project.Side == side)
            .ToDictionary(project => project.AbsolutePath, StringComparer.OrdinalIgnoreCase);
        var incoming = projects.Keys.ToDictionary(path => path, _ => 0, StringComparer.OrdinalIgnoreCase);
        var dependents = projects.Keys.ToDictionary(path => path, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects.Values)
        {
            foreach (var dependency in project.Dependencies)
            {
                if (dependency.TargetPath == null || !projects.ContainsKey(dependency.TargetPath))
                {
                    continue;
                }
                incoming[project.AbsolutePath]++;
                dependents[dependency.TargetPath].Add(project.AbsolutePath);
            }
        }

        var ready = new SortedSet<string>(
            incoming.Where(pair => pair.Value == 0).Select(pair => pair.Key),
            Comparer<string>.Create((left, right) =>
            {
                var comparison = StringComparer.Ordinal.Compare(projects[left].PluginId, projects[right].PluginId);
                return comparison != 0 ? comparison : StringComparer.OrdinalIgnoreCase.Compare(left, right);
            }));
        var sorted = new List<ProjectInfo>(projects.Count);
        while (ready.Count > 0)
        {
            var path = ready.Min!;
            ready.Remove(path);
            sorted.Add(projects[path]);
            foreach (var dependent in dependents[path])
            {
                incoming[dependent]--;
                if (incoming[dependent] == 0)
                {
                    ready.Add(dependent);
                }
            }
        }

        if (sorted.Count != projects.Count)
        {
            var cycleProjects = incoming.Where(pair => pair.Value > 0)
                .Select(pair => projects[pair.Key].PluginId)
                .OrderBy(id => id, StringComparer.Ordinal);
            errors.Add($"{side} module graph contains a cycle: {string.Join(", ", cycleProjects)}.");
        }
        return sorted;
    }

    private static bool IsSideAllowed(ProjectSide from, ProjectSide to)
    {
        return to == ProjectSide.Unknown ||
               from == to ||
               (from is ProjectSide.Client or ProjectSide.Server && to == ProjectSide.Shared);
    }
}
