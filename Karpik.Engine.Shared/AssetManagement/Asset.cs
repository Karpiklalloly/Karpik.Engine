namespace Karpik.Engine.Shared;

public abstract class Asset
{
    public int Id { get; internal set; }
    public Type SourceType { get; internal set; } 
    public string Path { get; internal set; }
    public int RefCount { get; private set; } = 0;

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
}