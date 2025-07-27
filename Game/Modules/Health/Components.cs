using DCFApixels.DragonECS;
using Karpik.Engine.Shared.DragonECS;
using Karpik.StatAndAbilities;

namespace Karpik.Game.Modules;

[EzRangeStat]
public partial struct Health { }

[Stat]
public partial struct Damage { }

public struct DealDamageRequest : IEcsComponentRequest
{
    public int Target { get; set; }
    public double Damage { get; set; }
    public IEnumerable<int> Sources { get; set; }
}

[AllowedInWorlds(typeof(EcsEventWorld), nameof(EcsEventWorld))]
public struct DealDamageEvent : IEcsComponentEvent
{
    public int Source { get; set; }
    public int Target { get; set; }
    public double Damage { get; set; }
}

[AllowedInWorlds(typeof(EcsEventWorld), nameof(EcsEventWorld))]
public struct KillEvent : IEcsComponentEvent
{
    public int Source { get; set; }
    public int Target { get; set; }
}

public struct KillRequest : IEcsComponentRequest
{
    public int Target { get; set; }
    public IEnumerable<int> Sources { get; set; }
}

[Serializable]
public struct DealDamageOnContact : IEcsTagComponent;