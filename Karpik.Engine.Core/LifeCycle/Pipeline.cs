namespace Karpik.Engine.Core;

public class Pipeline
{
    private List<ISystemInit> _initSystems = new();
    private List<ISystemBegin> _beginSystems = new();
    private List<ISystemFixedUpdate> _fixedUpdateSystems = new();
    private List<ISystemUpdate> _updateSystems = new();
    private List<ISystemLate> _endSystems = new();
    private List<ISystemRender> _renderSystems = new();
    private List<ISystemDestroy> _destroySystems = new();
    
    public void Add(ISystemInit init) => _initSystems.Add(init);
    public void Add(ISystemBegin begin) => _beginSystems.Add(begin);
    public void Add(ISystemFixedUpdate fixedUpdate) => _fixedUpdateSystems.Add(fixedUpdate);
    public void Add(ISystemUpdate update) => _updateSystems.Add(update);
    public void Add(ISystemLate end) => _endSystems.Add(end);
    public void Add(ISystemRender render) => _renderSystems.Add(render);
    public void Add(ISystemDestroy destroy) => _destroySystems.Add(destroy);
    
    public void Init() => _initSystems.ForEach(static s => s.Init());
    public void Begin() => _beginSystems.ForEach(static s => s.Begin());
    public void FixedRun() => _fixedUpdateSystems.ForEach(static s => s.FixedUpdate());
    public void Update() => _updateSystems.ForEach(static s => s.Run());
    public void Late() => _endSystems.ForEach(static s => s.LateRun());
    public void Render() => _renderSystems.ForEach(static s => s.Render());
    public void Destroy() => _destroySystems.ForEach(static s => s.Destroy());
}