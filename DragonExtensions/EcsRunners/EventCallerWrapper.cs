using System.Buffers;
using System.Runtime.CompilerServices;
using DCFApixels.DragonECS.RunnersCore;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.DragonECS
{
    public static class EventsWrapper
    {
        public static IBuilder AddCaller<T>(this IBuilder b, string layerName = EcsConsts.BEGIN_LAYER)
            where T : struct, IEcsComponentEvent
        {
            b.Add(new EventCallerSystem<T>(b), layerName);
            return b;
        }
        
        private class EventCallerSystem<T> : IEcsRun, IEcsPipelineMember
            where T : struct, IEcsComponentEvent
        {
            public EcsPipeline Pipeline { get; set; }

            public EventCallerSystem(IBuilder b)
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
    
    public static class RequestsWrapper
    {
        public static IBuilder AddCaller<T>(this IBuilder b, string layerName = EcsConsts.BEGIN_LAYER)
            where T : struct, IEcsComponentRequest
        {
            b.Add(new EventCallerSystem<T>(b), layerName);
            return b;
        }
        
        private class EventCallerSystem<T> : IEcsRun, IEcsPipelineMember where T : struct, IEcsComponentRequest
        {
            public EcsPipeline Pipeline { get; set; }

            public EventCallerSystem(IBuilder b)
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
}