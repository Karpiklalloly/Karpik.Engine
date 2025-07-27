namespace Karpik.Engine.Shared.Modding;

public readonly struct ModMetaData(string id, string name, string author, string version, string description)
{
    [JsonProperty("id")]
    public readonly string Id = id;
    [JsonProperty("name")]
    public readonly string Name = name;
    [JsonProperty("description")]
    public readonly string Description = description;
    [JsonProperty("author")]
    public readonly string Author = author;
    [JsonProperty("version")]
    public readonly string Version = version;
}