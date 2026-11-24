namespace Karpik.Engine.Shared.Modding;

public interface IScriptRuntime : IDisposable
{
    public void Load();
    
    public void Start();

    /// <summary>Initializes VM-local state for a prepared code replacement; never repeats Start.</summary>
    public void Reload() { }

    public void Update(double dt);

    public void FixedUpdate(double fixedDt);

    public void Destroy();

    public void Unload();
}