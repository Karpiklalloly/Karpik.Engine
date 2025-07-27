using System.Buffers;
using System.Runtime.CompilerServices;
using DCFApixels.DragonECS.RunnersCore;

namespace Karpik.Engine.Shared.DragonECS
{
    public static class Events
    {
        public static EcsPipeline.Builder AddCaller<T>(this EcsPipeline.Builder b, string layerName = EcsConsts.BEGIN_LAYER)
            where T : struct, IEcsComponentEvent
        {
            b.AddUnique(new EventCallerSystem<T>(b), layerName);
            return b;
        }
        
        private class EventCallerSystem<T> : IEcsRun, IEcsPipelineMember
            where T : struct, IEcsComponentEvent
        {
            public EcsPipeline Pipeline { get; set; }

            public EventCallerSystem(EcsPipeline.Builder b)
            {
                b.AddRunner<OnEventRunner<T>>();
                b.AddRunner<OnEventsRunner<T>>();
            }

            public void Run()
            {
                Pipeline.GetRunner<OnEventRunner<T>>().Run();
                Pipeline.GetRunner<OnEventsRunner<T>>().Run();
            }
        }
        
        public class OnEventRunner<T> : EcsRunner<IEcsRunOnEvent<T>>, IEcsRunOnEvent<T>, IEcsInject<EcsEventWorld> where T : struct, IEcsComponentEvent
        {
            private class Aspect : EcsAspect
            {
                public EcsPool<T> evt = Inc;
            }

            private EcsEventWorld _eventWorld;

            public void Run()
            {
                var span = _eventWorld.Where(out Aspect a);
                if (span.Count == 0)
                {
                    return;
                }

                foreach (var e in span)
                {
                    foreach (var run in Process)
                    {
                        run.RunOnEvent(ref a.evt.Get(e));
                    }
                }
                
                //a.evt.ClearAll();
            }

            public void RunOnEvent(ref T evt)
            {
            }

            public void Inject(EcsEventWorld obj)
            {
                _eventWorld = obj;
            }
        }
        
        public class OnEventsRunner<T> : EcsRunner<IEcsRunOnEvents<T>>, IEcsRunOnEvents<T>, IEcsInject<EcsEventWorld>
            where T : struct, IEcsComponentEvent
        {
            private class Aspect : EcsAspect
            {
                public EcsPool<T> evt = Inc;
            }

            private EcsEventWorld _eventWorld;

            public void Run()
            {
                var span = _eventWorld.Where(out Aspect a);
                if (span.Count == 0)
                {
                    return;
                }

                T[] rentedArray = ArrayPool<T>.Shared.Rent(span.Count);
                try
                {
                    Span<T> events = rentedArray.AsSpan(0, span.Count);
                    for (int i = 0; i < span.Count; i++)
                    {
                        events[i] = a.evt.Get(span[i]);
                    }

                    foreach (var run in Process)
                    {
                        run.RunOnEvents(events);
                    }
                }
                finally
                {
                    ArrayPool<T>.Shared.Return(rentedArray);
                }
                
                a.evt.ClearAll();
            }

            public void Inject(EcsEventWorld obj)
            {
                _eventWorld = obj;
            }

            public void RunOnEvents(Span<T> events)
            {
                
            }
        }
        
        public class OnEventFixedRunner<T> : EcsRunner<IEcsFixedRunOnEvent<T>>, IEcsFixedRunOnEvent<T>, IEcsInject<EcsEventWorld> where T : struct, IEcsComponentEvent
        {
            private class Aspect : EcsAspect
            {
                public EcsPool<T> evt = Inc;
            }

            private EcsEventWorld _eventWorld;

            public void Run()
            {
                var span = _eventWorld.Where(out Aspect a);
                if (span.Count == 0)
                {
                    return;
                }

                foreach (var e in span)
                {
                    foreach (var run in Process)
                    {
                        run.RunOnEvent(ref a.evt.Get(e));
                    }
                }

                a.evt.ClearAll();
            }

            public void RunOnEvent(ref T evt)
            {
            }

            public void Inject(EcsEventWorld obj)
            {
                _eventWorld = obj;
            }
        }
    }
    
    public static class Requests
    {
        public static EcsPipeline.Builder AddCaller<T>(this EcsPipeline.Builder b, string layerName = EcsConsts.BEGIN_LAYER)
            where T : struct, IEcsComponentRequest
        {
            b.AddUnique(new EventCallerSystem<T>(b), layerName);
            return b;
        }
        
        private class EventCallerSystem<T> : IEcsRun, IEcsPipelineMember where T : struct, IEcsComponentRequest
        {
            public EcsPipeline Pipeline { get; set; }

            public EventCallerSystem(EcsPipeline.Builder b)
            {
                b.AddRunner<OnRequestRunner<T>>();
            }

            public void Run()
            {
                Pipeline.GetRunner<OnRequestRunner<T>>().Run();
            }
        }
        
        public class OnRequestRunner<T> : EcsRunner<IEcsRunOnRequest<T>>, IEcsRunOnRequest<T>, IEcsInject<EcsDefaultWorld> where T : struct, IEcsComponentRequest
        {
            private class Aspect : EcsAspect
            {
                public EcsPool<T> evt = Inc;
            }

            private EcsDefaultWorld _world;

            public void Run()
            {
                var span = _world.Where(out Aspect a);
                if (span.Count == 0)
                {
                    return;
                }

                foreach (var e in span)
                {
                    foreach (var run in Process)
                    {
                        run.RunOnRequest(ref a.evt.Get(e));
                    }
                }
                
                a.evt.ClearAll();
            }

            public void RunOnRequest(ref T evt)
            {
            }

            public void Inject(EcsDefaultWorld obj)
            {
                _world = obj;
            }
        }
        
        public class OnRequestFixedRunner<T> : EcsRunner<IEcsFixedRunOnRequest<T>>, IEcsFixedRunOnRequest<T>, IEcsInject<EcsDefaultWorld> where T : struct, IEcsComponentRequest
        {
            private class Aspect : EcsAspect
            {
                public EcsPool<T> evt = Inc;
            }

            private EcsDefaultWorld _world;

            public void Run()
            {
                var span = _world.Where(out Aspect a);
                if (span.Count == 0)
                {
                    return;
                }

                foreach (var e in span)
                {
                    foreach (var run in Process)
                    {
                        run.RunOnEvent(ref a.evt.Get(e));
                    }
                }
                
                a.evt.ClearAll();
            }

            public void RunOnEvent(ref T evt)
            {
            }

            public void Inject(EcsDefaultWorld obj)
            {
                _world = obj;
            }
        }
    }

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
    
    public abstract class FixedRunOnEventSystem<TEvent, TAspect> : IEcsFixedRunOnEvent<TEvent>, IEcsInject<EcsEventWorld>
        where TEvent : struct, IEcsComponentEvent
        where TAspect : EcsAspect, new()
    {
        private EcsEventWorld _world;
        
        public void RunOnEvent(ref TEvent evt)
        {
            var aspect = _world.GetAspect<TAspect>();
            if (aspect.IsMatches(evt.Target))
            {
                FixedRunOnEvent(ref evt, ref aspect);
            }
        }
        
        public abstract void FixedRunOnEvent(ref TEvent evt, ref TAspect aspect);
        public void Inject(EcsEventWorld obj)
        {
            _world = obj;
        }
    }
    
    public abstract class FixedRunOnRequestSystem<TRequest, TAspect> : IEcsFixedRunOnRequest<TRequest>,  IEcsInject<EcsDefaultWorld>
        where TRequest : struct, IEcsComponentRequest
        where TAspect : EcsAspect, new()
    {
        private EcsDefaultWorld _world;
        
        public void RunOnEvent(ref TRequest evt)
        {
            var aspect = _world.GetAspect<TAspect>();
            if (aspect.IsMatches(evt.Target))
            {
                FixedRunOnEvent(ref evt, ref aspect);
            }
        }
        
        public abstract void FixedRunOnEvent(ref TRequest evt, ref TAspect aspect);
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
    
    public interface IEcsFixedRunOnEvent<T> : IEcsProcess where T : struct, IEcsComponentEvent
    {
        public void RunOnEvent(ref T evt);
    }
    
    public interface IEcsRunOnRequest<T> : IEcsProcess where T : struct, IEcsComponentRequest
    {
        public void RunOnRequest(ref T evt);
    }
    
    public interface IEcsFixedRunOnRequest<T> : IEcsProcess where T : struct, IEcsComponentRequest
    {
        public void RunOnEvent(ref T evt);
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