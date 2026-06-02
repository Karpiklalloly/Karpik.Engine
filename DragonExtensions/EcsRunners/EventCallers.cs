using System.Runtime.CompilerServices;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.DragonECS
{
    public abstract class RunOnEventSystem<TEvent, TAspect> : IEcsRunOnEvent<TEvent>, IEcsInject<EcsDefaultWorld>
        where TEvent : struct, IEcsComponentEvent
        where TAspect : EcsAspect, new()
    {
        private EcsDefaultWorld _world;
        
        public void RunOnEvent(ref TEvent evt)
        {
            try
            {
                var aspect = _world.GetAspect<TAspect>();
                if (aspect.IsMatches(evt.Target))
                {
                    RunOnEvent(ref evt, ref aspect);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            
        }
        
        protected abstract void RunOnEvent(ref TEvent evt, ref TAspect aspect);
        public void Inject(EcsDefaultWorld obj)
        {
            _world = obj;
        }
    }
    
    public abstract class RunOnRequestSystem<TRequest, TAspect> : IEcsRunOnRequest<TRequest>, IEcsInject<EcsDefaultWorld>
        where TRequest : struct, IEcsComponentRequest
        where TAspect : EcsAspect, new()
    {
        private EcsDefaultWorld _world;
        
        public void RunOnRequest(ref TRequest evt)
        {
            try
            {
                var aspect = _world.GetAspect<TAspect>();
                if (aspect.IsMatches(evt.Target))
                {
                    RunOnEvent(ref evt, ref aspect);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            
        }
        
        protected abstract void RunOnEvent(ref TRequest evt, ref TAspect aspect);
        public void Inject(EcsDefaultWorld obj)
        {
            _world = obj;
        }
    }

    public interface IEcsRunOnEvent<T> : IEcsProcess where T : struct, IEcsComponentEvent
    {
        public void RunOnEvent(ref T evt);
    }
    
    public interface IEcsRunOnEvents<T> : IEcsProcess where T : struct, IEcsComponentEvent
    {
        public void RunOnEvents(Span<T> events);
    }
    
    public interface IEcsRunOnRequest<T> : IEcsProcess where T : struct, IEcsComponentRequest
    {
        public void RunOnRequest(ref T evt);
    }

    public interface IEcsComponentEvent : IEcsComponent
    {
        public int Source { get; set; }
        public int Target { get; set; }
    }
    
    public interface IEcsComponentRequest : IEcsComponent
    {
        public int Target { get; set; }
        public IEnumerable<int> Sources { get; set; }
    }

    public static class EventCallersWorldExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SendEvent<T>(this EcsEventWorld world, T evt) where T : struct, IEcsComponentEvent
        {
            world.GetPool<T>().Add(world.NewEntity()) = evt;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SendRequest<T>(this EcsDefaultWorld world, T evt) where T : struct, IEcsComponentRequest
        {
            world.GetPool<T>().TryAddOrGet(evt.Target) = evt;
        }
    }
}