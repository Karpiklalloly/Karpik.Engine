namespace Karpik.Engine.Shared.AssetManagement.Core;

public abstract class Asset
{
    public int Id { get; internal set; }
    public Type Type { get; internal set; }
    public abstract Type ValueType { get; }
    public string Path { get; internal set; }
    public int RefCount { get; private set; } = 0;
    public abstract object RawValue { get; set; }
    internal IAssetsManager Manager { get; set; }

    public List<Asset> Dependencies { get; } = new();

    protected internal Asset()
    {
        Type = GetType();
    }

    internal void IncrementRef()
    {
        RefCount++;
    }

    internal bool DecrementRef()
    {
        RefCount--;
        return RefCount <= 0;
    }

    internal void Unload()
    {
        OnUnload();
        
        if (Dependencies.Count > 0 && Manager != null)
        {
            foreach (var child in Dependencies)
            {
                Manager.ReleaseAsset(child);
            }
            Dependencies.Clear();
        }
    }

    internal void Load()
    {
        OnLoad();
    }
    
    protected virtual void OnUnload() { }
    protected virtual void OnLoad() { }

    public override string ToString()
    {
        return $"Asset {Id} with source type {Type}";
    }
}