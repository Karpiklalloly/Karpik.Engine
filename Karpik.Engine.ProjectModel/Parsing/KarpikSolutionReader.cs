using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Karpik.Engine.ProjectModel;

/// <summary>Читает solution и project-файлы в независимую от MSBuild модель Karpik.</summary>
public sealed class KarpikSolutionReader
{
    /// <summary>Сторожевое значение для некорректного enum-свойства проекта.</summary>
    private const int InvalidEnumValue = -1;

    /// <summary>Читает решение из локальной файловой системы.</summary>
    public KarpikSolutionModel Read(string solutionPath) => Read(solutionPath, FileSystemKarpikProjectInputProvider.Instance);

    /// <summary>Читает решение через заданный источник файлового ввода.</summary>
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

    /// <summary>Читает транзитивный граф ссылок, достижимый из одного проекта.</summary>
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

    /// <summary>Разрешает путь, объявленный в решении, и читает соответствующий проект.</summary>
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

    /// <summary>Читает один project-файл либо возвращает descriptor с причиной ошибки чтения.</summary>
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

    /// <summary>Находит общий корень direct-build graph, предпочитая каталог с global.json.</summary>
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

    /// <summary>Безопасно загружает XML, запрещая DTD и внешние XML-resolver'ы.</summary>
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

    /// <summary>Извлекает все SDK-имена из корня проекта, Sdk и Import элементов.</summary>
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

    /// <summary>Разбирает разделённые точкой с запятой объявления SDK и добавляет их имена.</summary>
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

    /// <summary>Читает единственное корректное enum-свойство из верхнеуровневых PropertyGroup.</summary>
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

    /// <summary>Извлекает и нормализует статически объявленные ссылки на проекты.</summary>
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

    /// <summary>Проверяет, что ссылка literal, unconditional и не использует MSBuild-выражения.</summary>
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

    /// <summary>Извлекает требования к Karpik-модулям из верхнеуровневых ItemGroup.</summary>
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

    /// <summary>Читает metadata из атрибута либо вложенного элемента MSBuild item.</summary>
    private static string? ReadMetadata(XElement element, string name)
    {
        return element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value.Trim()
               ?? element.Elements().FirstOrDefault(child => child.Name.LocalName == name)?.Value.Trim();
    }

    /// <summary>Перечисляет элементы первого уровня с заданным локальным именем.</summary>
    private static IEnumerable<XElement> TopLevelElements(XDocument document, string name)
    {
        return document.Root?.Elements().Where(element => element.Name.LocalName == name) ?? [];
    }

    /// <summary>Перечисляет item-элементы из верхнеуровневых ItemGroup по их именам.</summary>
    private static IEnumerable<XElement> TopLevelItemElements(XDocument document, params string[] names)
    {
        return TopLevelElements(document, "ItemGroup")
            .SelectMany(group => group.Elements()
                .Where(element => names.Contains(element.Name.LocalName, StringComparer.Ordinal)));
    }

    /// <summary>Читает закреплённую версию Karpik.Engine.Sdk из global.json.</summary>
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

    /// <summary>Создаёт descriptor проекта, который нельзя использовать в дальнейшей валидации.</summary>
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
