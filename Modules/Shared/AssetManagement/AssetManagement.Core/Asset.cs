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
    
    private readonly List<Asset> _dependencies = new();

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
    
    public void AddDependency(Asset child)
    {
        if (child is null) return;
        if (child == this) return;
#if DEBUG
        bool ChildHasDependencyOnParent(Asset child)
        {
            if (child._dependencies.Contains(this)) return true;
            foreach (var dep in child._dependencies)
            {
                if (ChildHasDependencyOnParent(dep)) return true;
            }
            return false;
        }

        if (ChildHasDependencyOnParent(child)) throw new Exception("Cyclic dependency detected");
#endif

        if (!_dependencies.Contains(child))
        {
            _dependencies.Add(child);
            child.IncrementRef();
            Logger.Instance.Log(Type.Name, $"Added dependency: {Path} -> {child.Path}", LogLevel.Debug);
        }
    }

    internal void Unload()
    {
        OnUnload();
        
        if (_dependencies.Count > 0 && Manager != null)
        {
            foreach (var child in _dependencies)
            {
                Manager.ReleaseAsset(child);
            }
            _dependencies.Clear();
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