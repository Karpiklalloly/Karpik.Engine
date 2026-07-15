using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Karpik.Engine.ProjectModel;

public sealed class KarpikSolutionReader
{
    private const int InvalidEnumValue = -1;

    public KarpikSolutionModel Read(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);

        var normalizedSolutionPath = KarpikPathPolicy.Normalize(solutionPath);
        var solutionRoot = Path.GetDirectoryName(normalizedSolutionPath)
                           ?? throw new ArgumentException("The solution path must have a parent directory.", nameof(solutionPath));
        var solution = LoadXml(normalizedSolutionPath);
        var projects = solution.Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => ReadProject(solutionRoot, path!))
            .ToList();

        return new KarpikSolutionModel(normalizedSolutionPath, ReadSdkVersion(solutionRoot), projects);
    }

    private static KarpikProjectDescriptor ReadProject(string solutionRoot, string declaredPath)
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
        if (!File.Exists(projectPath))
        {
            return InvalidDescriptor(projectPath, KarpikProjectReadStatus.Missing);
        }

        try
        {
            var document = LoadXml(projectPath);
            return new KarpikProjectDescriptor(
                projectPath,
                ReadSdkNames(document),
                ReadEnumProperty<KarpikProjectKind>(document, "KarpikProjectKind"),
                ReadEnumProperty<KarpikProjectSide>(document, "KarpikSide"),
                ReadProjectReferences(document, projectPath),
                ReadModules(document));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            return InvalidDescriptor(projectPath, KarpikProjectReadStatus.Unreadable);
        }
    }

    private static XDocument LoadXml(string path)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using var stream = File.OpenRead(path);
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static IReadOnlyList<string> ReadSdkNames(XDocument document)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rootSdk = document.Root?.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == "Sdk")?.Value;
        AddSdkDeclarations(names, rootSdk);

        foreach (var sdk in document.Descendants().Where(element => element.Name.LocalName == "Sdk"))
        {
            var name = sdk.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value;
            AddSdkDeclarations(names, name);
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
        var values = document.Descendants()
            .Where(element => element.Name.LocalName == propertyName)
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

    private static IReadOnlyList<string> ReadProjectReferences(XDocument document, string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var references = new List<string>();
        foreach (var reference in document.Descendants().Where(element => element.Name.LocalName == "ProjectReference"))
        {
            var include = reference.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Include")?.Value;
            if (!string.IsNullOrWhiteSpace(include))
            {
                references.Add(KarpikPathPolicy.Normalize(include, projectDirectory));
            }
        }
        return references;
    }

    private static IReadOnlyList<KarpikModuleReference> ReadModules(XDocument document)
    {
        var modules = new List<KarpikModuleReference>();
        foreach (var dependency in document.Descendants().Where(element => element.Name.LocalName == "KarpikModuleDependency"))
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

    private static string ReadSdkVersion(string solutionRoot)
    {
        var globalJsonPath = Path.Combine(solutionRoot, "global.json");
        if (!File.Exists(globalJsonPath))
        {
            return string.Empty;
        }

        try
        {
            using var stream = File.OpenRead(globalJsonPath);
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
