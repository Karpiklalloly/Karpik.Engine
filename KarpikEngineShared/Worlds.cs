namespace Karpik.Engine.Shared;

public class Worlds
{
    private static ThreadLocal<Worlds> _instance = new ThreadLocal<Worlds>(() => new Worlds());
    public static Worlds Instance => _instance.Value;

    private bool _inited = false;
    
    public EcsDefaultWorld World { get; } = new EcsDefaultWorld();
    public EcsEventWorld EventWorld { get; } = new EcsEventWorld();
    public MetaWorld MetaWorld { get; } = new MetaWorld();
    public EcsPipeline Pipeline { get; private set; }

    public void Init(EcsPipeline pipeline)
    {
        if (_inited) return;
        _inited = true;
        Pipeline = pipeline;
    }
}