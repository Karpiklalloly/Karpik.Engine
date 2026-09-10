using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Karpik.Engine.ProjectModel;

public sealed class KarpikSolutionReader
{
    private const int InvalidEnumValue = -1;

    public KarpikSolutionModel Read(string solutionPath) => Read(solutionPath, FileSystemKarpikProjectInputProvider.Instance);

    public KarpikSolutionModel Read(string solutionPath, IKarpikProjectInputProvider inputProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        ArgumentNullException.ThrowIfNull(inputProvider);

        string normalizedSolutionPath = KarpikPathPolicy.Normalize(solutionPath);
        string solutionRoot = Path.GetDirectoryName(normalizedSolutionPath)
                              ?? throw new ArgumentException("The solution path must have a parent directory.",
                                  nameof(solutionPath));
        XDocument solution = LoadXml(normalizedSolutionPath, inputProvider);
        List<KarpikProjectDescriptor> projects = solution.Descendants()
            .Where(static element => element.Name.LocalName == "Project")
            .Select(static element => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Path")?.Value)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
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

        string normalizedProjectPath = KarpikPathPolicy.Normalize(projectPath);
        List<KarpikProjectDescriptor> projects = new List<KarpikProjectDescriptor>();
        Queue<string> pending = new Queue<string>();
        HashSet<string> visited = new HashSet<string>(KarpikPathPolicy.Comparer);
        pending.Enqueue(normalizedProjectPath);

        while (pending.Count > 0)
        {
            string currentPath = pending.Dequeue();
            if (!visited.Add(currentPath))
            {
                continue;
            }

            KarpikProjectDescriptor project = ReadProjectFile(
                currentPath,
                FileSystemKarpikProjectInputProvider.Instance);
            projects.Add(project);
            foreach (string reference in project.ProjectReferences)
            {
                pending.Enqueue(reference);
            }
        }

        string graphRoot = FindGraphRoot(normalizedProjectPath, projects);
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
            XDocument document = LoadXml(projectPath, inputProvider);
            List<XElement> projectReferenceElements = TopLevelItemElements(document, "ProjectReference").ToList();
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
        string rootProjectDirectory = Path.GetDirectoryName(rootProjectPath)!;
        for (DirectoryInfo? directory = new DirectoryInfo(rootProjectDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }
        }

        string graphRoot = rootProjectDirectory;
        foreach (KarpikProjectDescriptor project in projects)
        {
            while (!KarpikPathPolicy.IsWithinRoot(project.ProjectPath, graphRoot))
            {
                string? parent = Directory.GetParent(graphRoot)?.FullName;
                if (parent == null)
                {
                    break;
                }

                graphRoot = parent;
            }
        }

        return graphRoot;
    }

    private static XDocument LoadXml(string path, IKarpikProjectInputProvider inputProvider)
    {
        XmlReaderSettings settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using Stream stream = inputProvider.OpenRead(path);
        using XmlReader reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static IReadOnlyList<string> ReadSdkNames(XDocument document)
    {
        HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? rootSdk = document.Root?.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == "Sdk")?.Value;
        AddSdkDeclarations(names, rootSdk);

        foreach (XElement sdk in TopLevelElements(document, "Sdk"))
        {
            string? name = sdk.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value;
            AddSdkDeclarations(names, name);
        }

        foreach (XElement import in TopLevelElements(document, "Import"))
        {
            string? sdk = import.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Sdk")?.Value;
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

        foreach (string declaration in declarations.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int versionSeparator = declaration.IndexOf('/');
            string name = versionSeparator > 0 ? declaration[..versionSeparator] : declaration;
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name.Trim());
            }
        }
    }

    private static TEnum ReadEnumProperty<TEnum>(XDocument document, string propertyName)
        where TEnum : struct, Enum
    {
        List<string> values = TopLevelElements(document, "PropertyGroup")
            .SelectMany(group => group.Elements().Where(element => element.Name.LocalName == propertyName))
            .Select(element => element.Value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (values.Count == 1 &&
            Enum.TryParse(values[0], ignoreCase: true, out TEnum value) &&
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
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        List<string> references = new List<string>();
        foreach (XElement reference in projectReferenceElements)
        {
            string? include = reference.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Include")
                ?.Value;
            if (!string.IsNullOrWhiteSpace(include))
            {
                references.Add(KarpikPathPolicy.Normalize(include, projectDirectory));
            }
        }

        return references;
    }

    private static bool IsStaticProjectReference(XElement reference)
    {
        string? include = reference.Attributes()
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
        List<KarpikModuleReference> modules = new List<KarpikModuleReference>();
        foreach (XElement dependency in TopLevelItemElements(
                     document,
                     "KarpikModuleDependency",
                     "KarpikModuleReference"))
        {
            string? id = ReadMetadata(dependency, "Include");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            modules.Add(new KarpikModuleReference(
                id,
                ReadMetadata(dependency, "Implementation"),
                bool.TryParse(ReadMetadata(dependency, "Optional"), out bool optional) && optional));
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
        string globalJsonPath = Path.Combine(solutionRoot, "global.json");
        if (!inputProvider.Exists(globalJsonPath))
        {
            return string.Empty;
        }

        try
        {
            using Stream stream = inputProvider.OpenRead(globalJsonPath);
            using JsonDocument document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("msbuild-sdks", out JsonElement sdkVersions) &&
                   sdkVersions.TryGetProperty("Karpik.Engine.Sdk", out JsonElement sdkVersion) &&
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