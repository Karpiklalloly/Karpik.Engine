namespace Karpik.Engine.Shared;

public class EcsMetaWorld : EcsWorld, IInjectionUnit
{
    private const string DEFAULT_NAME = "Meta";
    public EcsMetaWorld() : base(default(EcsWorldConfig), DEFAULT_NAME) { }
    public EcsMetaWorld(EcsWorldConfig config = null, string name = null, short worldID = -1) : base(config, name == null ? DEFAULT_NAME : name, worldID) { }
    public EcsMetaWorld(IConfigContainer configs, string name = null, short worldID = -1) : base(configs, name == null ? DEFAULT_NAME : name, worldID) { }
    void IInjectionUnit.InitInjectionNode(InjectionGraph nodes) { nodes.AddNode(this); }
}