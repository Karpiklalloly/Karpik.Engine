using System.Text.Json;
using Karpik.Launcher.Models;

namespace Karpik.Launcher.Services;

public sealed record TemplateCatalogResult(bool IsSuccess, string Message, IReadOnlyList<ProjectTemplate> Templates);

public sealed class ProjectTemplateCatalogReader
{
    public TemplateCatalogResult Read(string installationRoot)
    {
        string sdk = Path.Combine(Path.GetFullPath(installationRoot), "sdk");
        string path = Path.Combine(sdk, "templates.json");
        if (!File.Exists(path)) return new(true, "No project templates are supplied by this SDK.", []);
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            var templates = new List<ProjectTemplate>();
            foreach (JsonElement item in document.RootElement.GetProperty("templates").EnumerateArray())
            {
                string id = Read(item, "id");
                string name = Read(item, "name");
                string description = Read(item, "description");
                string shortName = Read(item, "shortName");
                string package = Read(item, "packageFile");
                if (package != Path.GetFileName(package) || !package.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(sdk, package)))
                    throw new InvalidDataException("Template package is invalid or missing.");
                templates.Add(new(id, name, description, shortName, package));
            }
            if (templates.GroupBy(template => template.Id, StringComparer.Ordinal).Any(group => group.Count() > 1) ||
                templates.GroupBy(template => template.ShortName, StringComparer.Ordinal).Any(group => group.Count() > 1))
                throw new InvalidDataException("Template identifiers must be unique.");
            return new(true, "Project templates loaded.", templates);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
        {
            return new(false, $"Cannot read SDK template catalog: {exception.Message}", []);
        }
    }

    private static string Read(JsonElement item, string name) => item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
        ? value.GetString()! : throw new InvalidDataException($"Template field '{name}' is required.");
}
