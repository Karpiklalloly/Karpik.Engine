using DCFApixels.DragonECS;
using SSSuperGame.Shared.CoinRush;
using Xunit;

namespace SSSuperGame.Tests;

/// <summary>Checks DragonECS aspect queries and concurrent reads.</summary>
public sealed class AspectProbeTests
{
    /// <summary>Selects entities with display state.</summary>
    private sealed class DisplayAspect : EcsAspect
    {
        public EcsPool<DisplayState> Displays = Inc;
    }

    /// <summary>Selects entities with player and display state.</summary>
    private sealed class PlayerAspect : EcsAspect
    {
        public EcsPool<Player> Players = Inc;
        public EcsPool<DisplayState> Displays = Inc;
    }

    /// <summary>Verifies a query for one component pool.</summary>
    [Fact]
    public void Single_pool_aspect_query()
    {
        var world = new EcsDefaultWorld();
        try
        {
            int e = world.NewEntity();
            world.GetPool<DisplayState>().Add(e) = new DisplayState { X = 1f };
            var span = world.Where(out DisplayAspect aspect);
            Assert.Equal(1, span.Count);
            Assert.Equal(1f, aspect.Displays.Read(span[0]).X);
        }
        finally
        {
            world.Destroy();
        }
    }

    /// <summary>Verifies a query that requires two component pools.</summary>
    [Fact]
    public void Two_pool_aspect_query()
    {
        var world = new EcsDefaultWorld();
        try
        {
            int e = world.NewEntity();
            world.GetPool<Player>().Add(e) = new Player { Index = 0 };
            world.GetPool<DisplayState>().Add(e) = new DisplayState { X = 2f };
            var span = world.Where(out PlayerAspect aspect);
            Assert.Equal(1, span.Count);
        }
        finally
        {
            world.Destroy();
        }
    }

    /// <summary>Verifies concurrent aspect queries against a shared ECS world.</summary>
    [Fact]
    public void Concurrent_queries_from_many_threads()
    {
        var world = new EcsDefaultWorld();
        try
        {
            for (int i = 0; i < 8; i++)
            {
                int e = world.NewEntity();
                world.GetPool<Player>().Add(e) = new Player { Index = i };
                world.GetPool<DisplayState>().Add(e) = new DisplayState { X = i };
            }
            world.Where(out DisplayAspect _);
            world.Where(out PlayerAspect _);
            int errors = 0;
            var tasks = new Task[8];
            for (int t = 0; t < tasks.Length; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    try
                    {
                        for (int i = 0; i < 500; i++)
                        {
                            var a = world.Where(out DisplayAspect da);
                            var b = world.Where(out PlayerAspect pa);
                            if (a.Count != 8 || b.Count != 8)
                            {
                                Interlocked.Increment(ref errors);
                            }
                        }
                    }
                    catch
                    {
                        Interlocked.Increment(ref errors);
                    }
                });
            }
            Task.WaitAll(tasks);
            Assert.Equal(0, errors);
        }
        finally
        {
            world.Destroy();
        }
    }
}
