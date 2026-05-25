using System.Drawing;
using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.Network.Core;
using Karpik.Engine.Shared.Physics.Core;
using Karpik.Engine.Shared.ECS;

namespace Karpik.Engine.MyGame.Server.Main.Systems;

/// <summary>
/// Server-side level initialization system - creates platforms and spawn points
/// </summary>
public class LevelInitSystem : ISystemInit
{
    private class PlatformAspect : EcsAspect
    {
        public EcsReadonlyPool<Platform> Platform = Inc;
    }

    private class NetworkIdAspect : EcsAspect
    {
        public EcsReadonlyPool<NetworkId> NetworkId = Inc;
    }

    [DI] private DefaultWorld _world = null!;
    [DI] private NetworkIdGenerator _networkIdGenerator = null!;

    public void Init()
    {
        _networkIdGenerator.EnsureAtLeast(GetMaxNetworkId());
        if (HasExistingLevel())
        {
            return;
        }

        CreatePlatforms();
        CreateSpawnPoints();
        CreateBoxes();
    }

    private bool HasExistingLevel()
    {
        return _world.Where(out PlatformAspect _).Count > 0;
    }

    private int GetMaxNetworkId()
    {
        var maxNetworkId = 0;
        foreach (var entity in _world.Where(out NetworkIdAspect aspect))
        {
            var networkId = aspect.NetworkId.Get(entity).Id;
            if (networkId > maxNetworkId)
            {
                maxNetworkId = networkId;
            }
        }

        return maxNetworkId;
    }

    private void CreatePlatforms()
    {
        // Ground platform
        CreatePlatform(new Vector2(0, -10), new Vector2(50, 2), "Ground");
        
        // Left wall
        CreatePlatform(new Vector2(-25, 0), new Vector2(2, 30), "LeftWall");
        
        // Right wall
        CreatePlatform(new Vector2(25, 0), new Vector2(2, 30), "RightWall");
        
        // Ceiling
        CreatePlatform(new Vector2(0, 15), new Vector2(50, 2), "Ceiling");
        
        // Platform 1 - low
        CreatePlatform(new Vector2(-5, -5), new Vector2(8, 1), "Platform1");
        
        // Platform 2 - middle left
        CreatePlatform(new Vector2(-12, 0), new Vector2(6, 1), "Platform2");
        
        // Platform 3 - middle right
        CreatePlatform(new Vector2(10, 2), new Vector2(6, 1), "Platform3");
        
        // Platform 4 - high left
        CreatePlatform(new Vector2(-8, 6), new Vector2(5, 1), "Platform4");
        
        // Platform 5 - high right
        CreatePlatform(new Vector2(12, 8), new Vector2(5, 1), "Platform5");
        
        // Small stepping platforms
        CreatePlatform(new Vector2(-3, -7), new Vector2(2, 0.5f), "Step1");
        CreatePlatform(new Vector2(3, -7), new Vector2(2, 0.5f), "Step2");
    }

    private void CreatePlatform(Vector2 position, Vector2 size, string name)
    {
        var entity = _world.New();
        
        // Add transform
        ref var transform = ref _world.Base.GetPool<Transform2D>().Add(entity.ID);
        transform.Position = position;
        transform.Rotation = 0;
        
        // Add body request - static platform
        ref var bodyRequest = ref _world.Base.GetPool<CreateBodyRequest>().Add(entity.ID);
        bodyRequest.BodyConfig = new BodyConfig
        {
            Type = BodyType.Static,
            Mass = 0,
            Friction = 0.8f,
            Restitution = 0.0f,
            IsSensor = false,
            CategoryBits = Physics2DLayers.Platform,
            MaskBits = Physics2DLayers.Player | Physics2DLayers.Platform
        };
        bodyRequest.ShapeConfig = ShapeConfig.Box(size);
        
        // Add platform tag
        _world.Base.GetPool<Platform>().Add(entity.ID);
        _world.Base.GetPool<NetworkId>().Add(entity.ID).Id = _networkIdGenerator.Next();
        _world.Base.GetPool<SpriteData>().Add(entity.ID) = new SpriteData()
        {
            Color = Color.White,
            TexturePath = "Platform.png",
            Size = size
        };
    }

    private void CreateSpawnPoints()
    {
        // Main spawn point (where players start)
        CreateSpawnPoint(new Vector2(0, -5), "MainSpawn");
        
        // Alternative spawn points
        CreateSpawnPoint(new Vector2(-10, 3), "AltSpawn1");
        CreateSpawnPoint(new Vector2(8, 5), "AltSpawn2");
    }

    private void CreateSpawnPoint(Vector2 position, string name)
    {
        var entity = _world.New();
        
        // Add transform
        ref var transform = ref _world.Base.GetPool<Transform2D>().Add(entity.ID);
        transform.Position = position;
        transform.Rotation = 0;
        
        // Add respawn point marker
        _world.Base.GetPool<RespawnPoint>().Add(entity.ID);
    }

    private void CreateBoxes()
    {
        // Some dynamic boxes that players can push
        CreateBox(new Vector2(-2, -5), new Vector2(1, 1), 1.0f);
        CreateBox(new Vector2(5, -5), new Vector2(1.5f, 1.5f), 2.0f);
        CreateBox(new Vector2(-8, 3), new Vector2(0.8f, 0.8f), 0.5f);
    }

    private void CreateBox(Vector2 position, Vector2 size, float mass)
    {
        var entity = _world.New();
        
        // Add transform
        ref var transform = ref _world.Base.GetPool<Transform2D>().Add(entity.ID);
        transform.Position = position;
        transform.Rotation = 0;
        
        // Add velocity for dynamic body
        ref var velocity = ref _world.Base.GetPool<Velocity2D>().Add(entity.ID);
        velocity.Linear = Vector2.Zero;
        velocity.Angular = 0;
        
        // Add body request - dynamic box
        ref var bodyRequest = ref _world.Base.GetPool<CreateBodyRequest>().Add(entity.ID);
        bodyRequest.BodyConfig = new BodyConfig
        {
            Type = BodyType.Dynamic,
            Mass = mass,
            Friction = 0.5f,
            Restitution = 0.2f,
            IsSensor = false,
            CategoryBits = Physics2DLayers.Platform,
            MaskBits = Physics2DLayers.Player | Physics2DLayers.Platform
        };
        bodyRequest.ShapeConfig = ShapeConfig.Box(size);
        
        // Add box tag for identification
        _world.Base.GetPool<PhysicsBox>().Add(entity.ID);
        _world.Base.GetPool<NetworkId>().Add(entity.ID).Id = _networkIdGenerator.Next();
        _world.Base.GetPool<SpriteData>().Add(entity.ID) = new SpriteData()
        {
            Color = Color.White,
            TexturePath = "Box.png",
            Size = size
        };
    }
}

/// <summary>
/// Platform tag component
/// </summary>
public struct Platform : IEcsComponent { }

/// <summary>
/// Physics box tag component
/// </summary>
public struct PhysicsBox : IEcsComponent { }
