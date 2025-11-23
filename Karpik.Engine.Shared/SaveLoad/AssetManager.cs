namespace Karpik.Engine.Shared;

public class AssetManager
{
    public string RootPath => AppDomain.CurrentDomain.BaseDirectory;
    public string ContentPath => Path.Combine(RootPath, "Content");
    public string ModsPath => Path.Combine(RootPath, "Mods");

    private Dictionary<Type, object> _registry = new();
    private readonly Dictionary<string, WeakReference<Stream>> _openStreams = new();
    private readonly Dictionary<Type, Func<Stream, object>> _convertersStream = new();
    private readonly Dictionary<Type, Func<string, object>> _convertersFileName = new();
    private readonly AssetsManager _assetsManager;

    public AssetManager()
    {
        _assetsManager = new AssetsManager();
    }
    

    public Stream GetStream(string relativePath)
    {
        string fullPath = Path.Combine(RootPath, relativePath);

        if (_openStreams.TryGetValue(fullPath, out var weakRef)
            && weakRef.TryGetTarget(out var stream)) return stream;
        if (!File.Exists(fullPath)) return Stream.Null;

        var s = File.OpenRead(fullPath);
        _openStreams[fullPath] = new WeakReference<Stream>(s);
        return s;
    }

    public byte[] ReadAllBytes(string relativePath)
    {
        using var stream = GetStream(relativePath);
        if  (stream == Stream.Null || stream.Length == 0) return [];
        
        byte[] buffer = new byte[stream.Length];
        stream.ReadExactly(buffer, 0, buffer.Length);
        return buffer;
    }
    
    public string ReadAllText(string relativePath)
    {
        using var stream = GetStream(relativePath);
        if (stream == Stream.Null) return string.Empty;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public T Load<T>(string relativePath)
    {
        if (!_convertersStream.TryGetValue(typeof(T), out var converter)) return default;
        
        using var stream = GetStream(relativePath);
        return (T)converter(stream);

    }

    public void RegisterConverter<T>(Func<Stream, object> converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        _convertersStream.Add(typeof(T), converter);
    }
    
    public void RegisterConverter<T>(Func<string, object> converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        _convertersFileName.Add(typeof(T), converter);
    }

    public void DisposeStreams()
    {
        foreach (var streamRef in _openStreams.Values)
        {
            if (streamRef.TryGetTarget(out var stream))
            {
                stream.Dispose();
            }
        }
        _openStreams.Clear();
    }
}