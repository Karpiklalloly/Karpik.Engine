namespace Karpik.Engine.Shared.Modding;

public interface IScriptRuntime : IDisposable
{
    public void Load();
    
    public void Start();

    public void Update(double dt);

    public void FixedUpdate(double fixedDt);

    public void Destroy();

    public void Unload();
}