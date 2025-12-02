namespace Karpik.Engine.Shared;

public abstract class Asset
{
    public int Id { get; internal set; }
    public Type Type { get; internal set; }
    public abstract Type ValueType { get; }
    public string Path { get; internal set; }
    public int RefCount { get; private set; } = 0;
    public abstract object RawValue { get; set; }

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

    protected abstract void OnUnload();

    internal void Unload()
    {
        OnUnload();
        Logger.Instance.Log($"Unload {Path}", LogLevel.Debug);
    }

    public override string ToString()
    {
        return $"Asset {Id} with source type {Type}";
    }
}