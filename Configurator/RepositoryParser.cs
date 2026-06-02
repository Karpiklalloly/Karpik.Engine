using System.Globalization;
using System.Xml.Linq;

namespace ProjectConfigurator;

public static class RepositoryParser
{
    private static readonly HashSet<string> GameRootPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "MyGame/MyGameResources/MyGameResources.csproj",
        "MyGame/Shared/MyGame.Shared.Main/MyGame.Shared.Main.csproj",
        "MyGame/Client/MyGame.Client.Main/MyGame.Client.Main.csproj",
        "MyGame/Server/MyGame.Server.Main/MyGame.Server.Main.csproj"
    };

    public static RepositoryModel Load(string rootPath, string solutionPath)
    {
        var errors = new List<string>();
        var projectPaths = XDocument.Load(solutionPath)
            .Descendants("Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizeRelativePath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var projects = new Dictionary<string, ProjectInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var relativePath in projectPaths)
        {
            var absolutePath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
            if (!File.Exists(absolutePath))
            {
                errors.Add($"Solution project does not exist: {relativePath}");
                continue;
            }

            projects[absolutePath] = ReadProject(rootPath, absolutePath, relativePath);
        }

        var candidates = projects.Values
            .Where(project => project.RelativePath.StartsWith("Modules/", StringComparison.OrdinalIgnoreCase))
            .Where(project => !project.IsTestProject && !project.IsExecutable)
            .OrderBy(project => project.PluginId, StringComparer.Ordinal)
            .ToList();
        var coreModuleIds = candidates
            .Where(project => project.PluginId.EndsWith(".Core", StringComparison.Ordinal))
            .Select(project => project.PluginId[..^".Core".Length])
            .ToHashSet(StringComparer.Ordinal);

        var modules = new Dictionary<string, ModuleInfo>(StringComparer.Ordinal);
        var plugins = new List<PluginInfo>();
        foreach (var project in candidates)
        {
            var plugin = InferPlugin(project, coreModuleIds);
            plugins.Add(plugin);

            if (!modules.TryGetValue(plugin.ModuleId, out var module))
            {
                module = new ModuleInfo { Id = plugin.ModuleId };
                modules.Add(plugin.ModuleId, module);
            }

            switch (plugin.Kind)
            {
                case PluginKind.Standalone:
                    if (module.Standalone != null)
                    {
                        errors.Add($"Duplicate standalone plugin for module {module.Id}.");
                    }
                    module.Standalone = plugin;
                    break;
                case PluginKind.Core:
                    if (module.Core != null)
                    {
                        errors.Add($"Duplicate core plugin for module {module.Id}.");
                    }
                    module.Core = plugin;
                    break;
                case PluginKind.Implementation:
                    if (!module.Implementations.TryAdd(plugin.ImplementationId!, plugin))
                    {
                        errors.Add($"Duplicate implementation {plugin.ImplementationId} for module {module.Id}.");
                    }
                    break;
            }
        }

        ResolveDependencies(projects.Values, plugins, errors);

        var propsPath = Path.Combine(rootPath, "Directory.Build.props");
        var (selections, settingValues) = ReadProfile(propsPath, errors);
        var gameRoots = projects.Values
            .Where(project => GameRootPaths.Contains(project.RelativePath))
            .OrderBy(project => project.PluginId, StringComparer.Ordinal)
            .ToList();

        foreach (var path in GameRootPaths)
        {
            if (!gameRoots.Any(project => project.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"Required game root is missing from the solution: {path}");
            }
        }

        foreach (var project in projects.Values.Where(project =>
                     project.RelativePath.StartsWith("MyGame/", StringComparison.OrdinalIgnoreCase) &&
                     !GameRootPaths.Contains(project.RelativePath)))
        {
            errors.Add($"Unknown game project: {project.RelativePath}");
        }

        return new RepositoryModel
        {
            RootPath = rootPath,
            SolutionPath = solutionPath,
            PropsPath = propsPath,
            ProjectsByPath = projects,
            Plugins = plugins,
            Modules = modules,
            GameRoots = gameRoots,
            Selections = selections,
            SettingValues = settingValues,
            ParseErrors = errors
        };
    }

    public static void SaveProfile(RepositoryModel model, IEnumerable<ModuleSelection> selections)
    {
        var document = XDocument.Load(model.PropsPath, LoadOptions.PreserveWhitespace);
        var root = document.Root!;
        root.Elements("ItemGroup")
            .Where(group => string.Equals((string?)group.Attribute("Label"), "ProjectConfigurator", StringComparison.Ordinal))
            .Remove();

        var group = new XElement("ItemGroup", new XAttribute("Label", "ProjectConfigurator"));
        foreach (var selection in selections.OrderBy(selection => selection.ModuleId, StringComparer.Ordinal))
        {
            var element = new XElement("KarpikModuleSelection",
                new XAttribute("Include", selection.ModuleId),
                new XAttribute("Enabled", selection.Enabled.ToString().ToLowerInvariant()));
            if (!string.IsNullOrWhiteSpace(selection.Implementation))
            {
                element.Add(new XAttribute("Implementation", selection.Implementation));
            }
            group.Add(element);
        }

        foreach (var value in model.SettingValues.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            group.Add(new XElement("KarpikModuleSettingValue",
                new XAttribute("Include", value.Key),
                new XAttribute("Value", value.Value)));
        }

        root.Add(group);
        document.Save(model.PropsPath);
    }

    private static ProjectInfo ReadProject(string rootPath, string absolutePath, string relativePath)
    {
        var document = XDocument.Load(absolutePath);
        string? GetProperty(string name) => document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim();
        var project = new ProjectInfo
        {
            AbsolutePath = absolutePath,
            RelativePath = relativePath,
            PluginId = Path.GetFileNameWithoutExtension(relativePath),
            Side = GetSide(relativePath),
            IsExecutable = string.Equals(GetProperty("OutputType"), "Exe", StringComparison.OrdinalIgnoreCase),
            IsTestProject = bool.TryParse(GetProperty("IsTestProject"), out var isTest) && isTest,
            AssemblyName = GetProperty("AssemblyName")
        };

        foreach (var reference in document.Descendants().Where(element => element.Name.LocalName == "ProjectReference"))
        {
            var target = ResolveReference(absolutePath, reference.Attribute("Include")?.Value);
            if (target != null)
            {
                project.DirectProjectReferences.Add(target);
            }
        }

        foreach (var dependency in document.Descendants().Where(element => element.Name.LocalName == "KarpikModuleDependency"))
        {
            var id = dependency.Attribute("Include")?.Value;
            if (!string.IsNullOrWhiteSpace(id))
            {
                project.Dependencies.Add(new DependencySpec(id, null, ReadBoolMetadata(dependency, "Optional")));
            }
        }

        foreach (var setting in document.Descendants().Where(element => element.Name.LocalName == "KarpikModuleSetting"))
        {
            var name = setting.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var values = ReadMetadata(setting, "Values") ?? ReadMetadata(setting, "AllowedValues") ?? "";
            project.Settings.Add(new SettingSchema(
                "",
                name,
                ReadMetadata(setting, "Type") ?? "String",
                ReadMetadata(setting, "Default"),
                ReadBoolMetadata(setting, "Required"),
                values.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
        }

        return project;
    }

    private static void ResolveDependencies(
        IEnumerable<ProjectInfo> projects,
        IEnumerable<PluginInfo> plugins,
        List<string> errors)
    {
        var projectsById = new Dictionary<string, List<ProjectInfo>>(StringComparer.Ordinal);
        foreach (var project in projects)
        {
            Add(project.PluginId, project);
        }
        foreach (var plugin in plugins.Where(plugin => plugin.Kind == PluginKind.Core))
        {
            Add(plugin.ModuleId, plugin.Project);
        }
        foreach (var pair in projectsById.Where(pair => pair.Value.Count > 1))
        {
            errors.Add(
                $"Ambiguous project id '{pair.Key}': " +
                $"{string.Join(", ", pair.Value.Select(match => match.RelativePath).OrderBy(path => path, StringComparer.Ordinal))}.");
        }

        foreach (var project in projects)
        {
            for (var index = 0; index < project.Dependencies.Count; index++)
            {
                var dependency = project.Dependencies[index];
                if (!projectsById.TryGetValue(dependency.Id, out var matches))
                {
                    errors.Add($"{project.RelativePath}: unknown KarpikModuleDependency id '{dependency.Id}'.");
                    continue;
                }
                if (matches.Count != 1)
                {
                    errors.Add(
                        $"{project.RelativePath}: ambiguous KarpikModuleDependency id '{dependency.Id}': " +
                        $"{string.Join(", ", matches.Select(match => match.RelativePath).OrderBy(path => path, StringComparer.Ordinal))}.");
                    continue;
                }
                project.Dependencies[index] = dependency with { TargetPath = matches[0].AbsolutePath };
            }
        }
        return;

        void Add(string id, ProjectInfo project)
        {
            if (!projectsById.TryGetValue(id, out var matches))
            {
                matches = [];
                projectsById.Add(id, matches);
            }
            if (!matches.Contains(project))
            {
                matches.Add(project);
            }
        }
    }

    private static PluginInfo InferPlugin(ProjectInfo project, HashSet<string> coreModuleIds)
    {
        if (project.PluginId.EndsWith(".Core", StringComparison.Ordinal))
        {
            return new PluginInfo
            {
                Project = project,
                ModuleId = project.PluginId[..^".Core".Length],
                Kind = PluginKind.Core
            };
        }

        foreach (var moduleId in coreModuleIds.OrderByDescending(id => id.Length))
        {
            var prefix = moduleId + ".";
            if (project.PluginId.StartsWith(prefix, StringComparison.Ordinal))
            {
                return new PluginInfo
                {
                    Project = project,
                    ModuleId = moduleId,
                    Kind = PluginKind.Implementation,
                    ImplementationId = project.PluginId[prefix.Length..]
                };
            }
        }

        return new PluginInfo
        {
            Project = project,
            ModuleId = project.PluginId,
            Kind = PluginKind.Standalone
        };
    }

    private static (Dictionary<string, ModuleSelection>, Dictionary<string, string>) ReadProfile(
        string propsPath,
        List<string> errors)
    {
        var selections = new Dictionary<string, ModuleSelection>(StringComparer.Ordinal);
        var settingValues = new Dictionary<string, string>(StringComparer.Ordinal);
        var document = XDocument.Load(propsPath);
        foreach (var element in document.Descendants().Where(element => element.Name.LocalName == "KarpikModuleSelection"))
        {
            var id = element.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                errors.Add("KarpikModuleSelection is missing Include.");
                continue;
            }

            var enabledText = ReadMetadata(element, "Enabled");
            if (!bool.TryParse(enabledText, out var enabled))
            {
                errors.Add($"Module selection {id} has invalid Enabled value '{enabledText}'.");
                continue;
            }

            if (!selections.TryAdd(id, new ModuleSelection
                {
                    ModuleId = id,
                    Enabled = enabled,
                    Implementation = ReadMetadata(element, "Implementation")
                }))
            {
                errors.Add($"Duplicate module selection: {id}");
            }
        }

        foreach (var element in document.Descendants().Where(element => element.Name.LocalName == "KarpikModuleSettingValue"))
        {
            var id = element.Attribute("Include")?.Value;
            var value = ReadMetadata(element, "Value");
            if (string.IsNullOrWhiteSpace(id) || value == null)
            {
                errors.Add("KarpikModuleSettingValue requires Include and Value.");
                continue;
            }

            if (!settingValues.TryAdd(id, value))
            {
                errors.Add($"Duplicate module setting value: {id}");
            }
        }

        return (selections, settingValues);
    }

    private static string? ResolveReference(string projectPath, string? include)
    {
        if (string.IsNullOrWhiteSpace(include) || include.Contains("$(", StringComparison.Ordinal))
        {
            return null;
        }

        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, include));
    }

    private static string? ReadMetadata(XElement element, string name)
    {
        return element.Attribute(name)?.Value ?? element.Elements().FirstOrDefault(child => child.Name.LocalName == name)?.Value;
    }

    private static bool ReadBoolMetadata(XElement element, string name)
    {
        return bool.TryParse(ReadMetadata(element, name), out var result) && result;
    }

    public static ProjectSide GetSide(string path)
    {
        var normalized = NormalizeRelativePath(path);
        if (normalized.StartsWith("Modules/Client/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("MyGame/Client/", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectSide.Client;
        }
        if (normalized.StartsWith("Modules/Server/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("MyGame/Server/", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectSide.Server;
        }
        if (normalized.StartsWith("Modules/Shared/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("MyGame/Shared/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("MyGame/MyGameResources/", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectSide.Shared;
        }
        return ProjectSide.Unknown;
    }

    public static string NormalizeRelativePath(string path) => path.Replace('\\', '/');
}
