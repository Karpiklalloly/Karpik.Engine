namespace Karpik.Engine.Shared;

public class MetaWorld : EcsWorld, IInjectionUnit
{
    private const string DEFAULT_NAME = "Meta";
    public MetaWorld() : base(default(EcsWorldConfig), DEFAULT_NAME) { }
    public MetaWorld(EcsWorldConfig config = null, string name = null, short worldID = -1) : base(config, name == null ? DEFAULT_NAME : name, worldID) { }
    public MetaWorld(IConfigContainer configs, string name = null, short worldID = -1) : base(configs, name == null ? DEFAULT_NAME : name, worldID) { }
    void IInjectionUnit.InitInjectionNode(InjectionGraph nodes) { nodes.AddNode(this); }
}