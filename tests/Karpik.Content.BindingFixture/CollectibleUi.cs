using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

public sealed class CollectibleUi
{
    public string Title { get; set; } = "";
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CollectibleUi))]
public partial class CollectibleJsonContext : JsonSerializerContext;

public static class MetadataFactory
{
    public static JsonTypeInfo Create() => new CollectibleJsonContext().CollectibleUi;
}
