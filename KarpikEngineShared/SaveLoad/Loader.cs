using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Karpik.Engine.Shared;

public class Loader
{
    private static ThreadLocal<Loader> _instance = new ThreadLocal<Loader>(() => new Loader());
    public static Loader Instance => _instance.Value;
    
    public AssetManager Manager;

    public string RootPath => Manager.RootPath;

    public void Serialize<T>(T obj, string relativePath)
    {
        var settings = new JsonSerializerSettings
        {
            Converters = { new ComponentArrayConverter(), new StringEnumConverter() }, // Один конвертер для всего массива
            Formatting = Formatting.Indented,
            
        };
        relativePath = ApproveFileName(relativePath, "json");
        relativePath = Path.Combine(Manager.RootPath, relativePath);
        if (!File.Exists(relativePath)) File.Create(relativePath);
        var json = JsonConvert.SerializeObject(obj, settings);
        File.WriteAllText(relativePath, json);
    }
    
    public T Load<T>(string path) => Manager.Load<T>(path);
    
    public JObject Load(string path) 
    {
        path = ApproveFileName(path, "json");
        var json = Manager.ReadAllText(path);
        return JObject.Parse(json);
    }
    
    private string ApproveFileName(string path, string extension)
    {
        extension = $".{extension}";
        if (path[^extension.Length..] != extension)
        {
            return path + extension;
        }
        return path;
    }
}