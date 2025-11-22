using System.Runtime.CompilerServices;

namespace Karpik.Engine.Shared
{
    public static class CoreComponentExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T Get<T>(this entlong entity) where T : struct, IEcsComponent
        {
            return ref entity.World.GetPool<T>().Get(entity.ID);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T Add<T>(this entlong entity) where T : struct, IEcsComponent
        {
            return ref entity.World.GetPool<T>().Add(entity.ID);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Has<T>(this entlong entity) where T : struct, IEcsComponent
        {
            return entity.World.GetPool<T>().Has(entity.ID);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Del<T>(this entlong entity) where T : struct, IEcsComponent
        {
            entity.World.GetPool<T>().Del(entity.ID);
        }
    }

    public static class CoreComponentExtensionsTag
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Add<T>(this entlong entity) where T : struct, IEcsTagComponent
        {
            entity.World.GetPool<T>().Add(entity.ID);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Has<T>(this entlong entity) where T : struct, IEcsTagComponent
        {
            return entity.World.GetPool<T>().Has(entity.ID);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Del<T>(this entlong entity) where T : struct, IEcsTagComponent
        {
            entity.World.GetPool<T>().Del(entity.ID);
        }
    }
}
