using System.Text;

namespace Karpik.Engine.Shared;

public class JsonSaver : IAssetSaver
{
    protected JsonSerializer Serializer { get; } = new();

    public JsonSaver()
    {
        Serializer.Formatting = Formatting.Indented; 
    }

    public async Task SaveAsync(Asset asset, Stream stream)
    {
        await Task.Run(() =>
        {
            using var sw = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true);
            using var jsonWriter = new JsonTextWriter(sw);
            Serializer.Serialize(jsonWriter, asset);
        });
    }
}