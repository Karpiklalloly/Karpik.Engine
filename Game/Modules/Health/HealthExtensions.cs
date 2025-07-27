using System.Runtime.CompilerServices;
using DCFApixels.DragonECS;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Game.Modules;

public partial class HealthExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DealDamageTo(this entlong source, entlong target, float damage, EcsEventWorld eventWorld)
    {
        eventWorld.SendEvent(new DealDamageEvent()
        {
            Damage = damage,
            Target = target.ID,
            Source = source.ID,
        });
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TakeDamageFrom(this entlong target, entlong source, float damage, EcsEventWorld eventWorld)
    {
        source.DealDamageTo(target, damage, eventWorld);
    }
}