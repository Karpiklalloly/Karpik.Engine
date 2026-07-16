using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Karpik.Engine.ProjectModel;

public sealed class KarpikSolutionReader
{
    private const int InvalidEnumValue = -1;

    public KarpikSolutionModel Read(string solutionPath)
        => Read(solutionPath, FileSystemKarpikProjectInputProvider.Instance);

    public KarpikSolutionModel Read(
        string solutionPath,
        IKarpikProjectInputProvider inputProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        ArgumentNullException.ThrowIfNull(inputProvider);

        var normalizedSolutionPath = KarpikPathPolicy.Normalize(solutionPath);
        var solutionRoot = Path.GetDirectoryName(normalizedSolutionPath)
                           ?? throw new ArgumentException("The solution path must have a parent directory.", nameof(solutionPath));
        var solution = LoadXml(normalizedSolutionPath, inputProvider);
        var projects = solution.Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => ReadProject(solutionRoot, path!, inputProvider))
            .ToList();

        return new KarpikSolutionModel(
            normalizedSolutionPath,
            ReadSdkVersion(solutionRoot, inputProvider),
            projects);
    }

    public KarpikSolutionModel ReadProjectGraph(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        var normalizedProjectPath = KarpikPathPolicy.Normalize(projectPath);
        var projects = new List<KarpikProjectDescriptor>();
        var pending = new Queue<string>();
        var visited = new HashSet<string>(KarpikPathPolicy.Comparer);
        pending.Enqueue(normalizedProjectPath);

        while (pending.Count > 0)
        {
            var currentPath = pending.Dequeue();
            if (!visited.Add(currentPath))
            {
                continue;
            }

            var project = ReadProjectFile(
                currentPath,
                FileSystemKarpikProjectInputProvider.Instance);
            projects.Add(project);
            foreach (var reference in project.ProjectReferences)
            {
                pending.Enqueue(reference);
            }
        }

        var graphRoot = FindGraphRoot(normalizedProjectPath, projects);
        return new KarpikSolutionModel(
            Path.Combine(graphRoot, ".karpik-direct-build.slnx"),
            ReadSdkVersion(graphRoot, FileSystemKarpikProjectInputProvider.Instance),
            projects);
    }

    private static KarpikProjectDescriptor ReadProject(
        string solutionRoot,
        string declaredPath,
        IKarpikProjectInputProvider inputProvider)
    {
        string projectPath;
        try
        {
            projectPath = KarpikPathPolicy.Normalize(declaredPath, solutionRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return InvalidDescriptor(declaredPath, KarpikProjectReadStatus.Unreadable);
        }

        if (!KarpikPathPolicy.IsWithinRoot(projectPath, solutionRoot))
        {
            return InvalidDescriptor(projectPath, KarpikProjectReadStatus.OutsideSolutionRoot);
        }

        return ReadProjectFile(projectPath, inputProvider);
    }

    private static KarpikProjectDescriptor ReadProjectFile(
        string projectPath,
        IKarpikProjectInputProvider inputProvider)
    {
        if (!inputProvider.Exists(projectPath))
        {
            return InvalidDescriptor(projectPath, KarpikProjectReadStatus.Missing);
        }

        try
        {
            var document = LoadXml(projectPath, inputProvider);
            var projectReferenceElements = TopLevelItemElements(document, "ProjectReference").ToList();
            return new KarpikProjectDescriptor(
                projectPath,
                ReadSdkNames(document),
                ReadEnumProperty<KarpikProjectKind>(document, "KarpikProjectKind"),
                ReadEnumProperty<KarpikProjectSide>(document, "KarpikSide"),
                ReadProjectReferences(projectReferenceElements, projectPath),
                ReadModules(document))
            {
                ProjectReferencesAreStatic = projectReferenceElements.All(IsStaticProjectReference)
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            return InvalidDescriptor(projectPath, KarpikProjectReadStatus.Unreadable);
        }
    }

    private static string FindGraphRoot(
        string rootProjectPath,
        IReadOnlyList<KarpikProjectDescriptor> projects)
    {
        var rootProjectDirectory = Path.GetDirectoryName(rootProjectPath)!;
        for (var directory = new DirectoryInfo(rootProjectDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }
        }

        var graphRoot = rootProjectDirectory;
        foreach (var project in projects)
        {
            while (!KarpikPathPolicy.IsWithinRoot(project.ProjectPath, graphRoot))
            {
                var parent = Directory.GetParent(graphRoot)?.FullName;
                if (parent == null)
                {
                    break;
                }
                graphRoot = parent;
            }
        }
        return graphRoot;
    }

    private static XDocument LoadXml(
        string path,
        IKarpikProjectInputProvider inputProvider)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using var stream = inputProvider.OpenRead(path);
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static IReadOnlyList<string> ReadSdkNames(XDocument document)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rootSdk = document.Root?.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == "Sdk")?.Value;
        AddSdkDeclarations(names, rootSdk);

        foreach (var sdk in TopLevelElements(document, "Sdk"))
        {
            var name = sdk.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value;
            AddSdkDeclarations(names, name);
        }

        foreach (var import in TopLevelElements(document, "Import"))
        {
            var sdk = import.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Sdk")?.Value;
            AddSdkDeclarations(names, sdk);
        }

        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddSdkDeclarations(ISet<string> names, string? declarations)
    {
        if (string.IsNullOrWhiteSpace(declarations))
        {
            return;
        }

        foreach (var declaration in declarations.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var versionSeparator = declaration.IndexOf('/');
            var name = versionSeparator > 0 ? declaration[..versionSeparator] : declaration;
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name.Trim());
            }
        }
    }

    private static TEnum ReadEnumProperty<TEnum>(XDocument document, string propertyName)
        where TEnum : struct, Enum
    {
        var values = TopLevelElements(document, "PropertyGroup")
            .SelectMany(group => group.Elements().Where(element => element.Name.LocalName == propertyName))
            .Select(element => element.Value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (values.Count == 1 &&
            Enum.TryParse<TEnum>(values[0], ignoreCase: true, out var value) &&
            Enum.IsDefined(value))
        {
            return value;
        }
        return (TEnum)Enum.ToObject(typeof(TEnum), InvalidEnumValue);
    }

    private static IReadOnlyList<string> ReadProjectReferences(
        IEnumerable<XElement> projectReferenceElements,
        string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var references = new List<string>();
        foreach (var reference in projectReferenceElements)
        {
            var include = reference.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Include")?.Value;
            if (!string.IsNullOrWhiteSpace(include))
            {
                references.Add(KarpikPathPolicy.Normalize(include, projectDirectory));
            }
        }
        return references;
    }

    private static bool IsStaticProjectReference(XElement reference)
    {
        var include = reference.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == "Include")?.Value;
        return !string.IsNullOrWhiteSpace(include) &&
               reference.Attributes().All(attribute => attribute.Name.LocalName != "Condition") &&
               reference.Parent!.Attributes().All(attribute => attribute.Name.LocalName != "Condition") &&
               !include.Contains("$(", StringComparison.Ordinal) &&
               !include.Contains("@(", StringComparison.Ordinal) &&
               !include.Contains("%(", StringComparison.Ordinal) &&
               include.IndexOfAny(['*', '?', ';']) < 0;
    }

    private static IReadOnlyList<KarpikModuleReference> ReadModules(XDocument document)
    {
        var modules = new List<KarpikModuleReference>();
        foreach (var dependency in TopLevelItemElements(
                     document,
                     "KarpikModuleDependency",
                     "KarpikModuleReference"))
        {
            var id = ReadMetadata(dependency, "Include");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }
            modules.Add(new KarpikModuleReference(
                id,
                ReadMetadata(dependency, "Implementation"),
                bool.TryParse(ReadMetadata(dependency, "Optional"), out var optional) && optional));
        }
        return modules;
    }

    private static string? ReadMetadata(XElement element, string name)
    {
        return element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value.Trim()
               ?? element.Elements().FirstOrDefault(child => child.Name.LocalName == name)?.Value.Trim();
    }

    private static IEnumerable<XElement> TopLevelElements(XDocument document, string name)
    {
        return document.Root?.Elements().Where(element => element.Name.LocalName == name) ?? [];
    }

    private static IEnumerable<XElement> TopLevelItemElements(XDocument document, params string[] names)
    {
        return TopLevelElements(document, "ItemGroup")
            .SelectMany(group => group.Elements()
                .Where(element => names.Contains(element.Name.LocalName, StringComparer.Ordinal)));
    }

    private static string ReadSdkVersion(
        string solutionRoot,
        IKarpikProjectInputProvider inputProvider)
    {
        var globalJsonPath = Path.Combine(solutionRoot, "global.json");
        if (!inputProvider.Exists(globalJsonPath))
        {
            return string.Empty;
        }

        try
        {
            using var stream = inputProvider.OpenRead(globalJsonPath);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("msbuild-sdks", out var sdkVersions) &&
                   sdkVersions.TryGetProperty("Karpik.Engine.Sdk", out var sdkVersion) &&
                   sdkVersion.ValueKind == JsonValueKind.String
                ? sdkVersion.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return string.Empty;
        }
    }

    private static KarpikProjectDescriptor InvalidDescriptor(string projectPath, KarpikProjectReadStatus status)
    {
        return new KarpikProjectDescriptor(
            projectPath,
            [],
            (KarpikProjectKind)InvalidEnumValue,
            (KarpikProjectSide)InvalidEnumValue,
            [],
            [])
        {
            ReadStatus = status
        };
    }
}
