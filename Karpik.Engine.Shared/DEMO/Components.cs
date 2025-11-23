using Network;

namespace Karpik.Engine.Shared.DEMO;

[NetworkedComponent]
public struct Health : IEcsComponent
{
    [NetworkedField]
    public double Value;
}

public struct TookDamageEvent : IEcsComponent
{
    
}

[NetworkedComponent]
public struct Player : IEcsComponent;    

public struct LocalPlayer : IEcsComponent;