using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.MyGame.Server.Main.Systems;

/// <summary>
/// Server-side level initialization system - creates platforms and spawn points
/// </summary>
public class LevelInitSystem : IEcsInit
{
    // Platform layer mask for collisions
    private const uint PLATFORM_LAYER = 0x0001;
    private const uint PLAYER_LAYER = 0x0002;
    
    [DI] private EcsDefaultWorld _world = null!;

    public void Init()
    {
        CreatePlatforms();
        CreateSpawnPoints();
        CreateBoxes();
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
        var entity = _world.NewEntity();
        
        // Add transform
        ref var transform = ref _world.GetPool<Transform2D>().Add(entity);
        transform.Position = position;
        transform.Rotation = 0;
        
        // Add body request - static platform
        ref var bodyRequest = ref _world.GetPool<CreateBodyRequest>().Add(entity);
        bodyRequest.BodyConfig = new BodyConfig
        {
            Type = BodyType.Static,
            Mass = 0,
            Friction = 0.8f,
            Restitution = 0.0f,
            IsSensor = false,
            CategoryBits = PLATFORM_LAYER,
            MaskBits = PLAYER_LAYER // Collides with player
        };
        bodyRequest.ShapeConfig = ShapeConfig.Box(size);
        
        // Add platform tag
        _world.GetPool<Platform>().Add(entity);
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
        var entity = _world.NewEntity();
        
        // Add transform
        ref var transform = ref _world.GetPool<Transform2D>().Add(entity);
        transform.Position = position;
        transform.Rotation = 0;
        
        // Add respawn point marker
        _world.GetPool<RespawnPoint>().Add(entity);
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
        var entity = _world.NewEntity();
        
        // Add transform
        ref var transform = ref _world.GetPool<Transform2D>().Add(entity);
        transform.Position = position;
        transform.Rotation = 0;
        
        // Add velocity for dynamic body
        ref var velocity = ref _world.GetPool<Velocity2D>().Add(entity);
        velocity.Linear = Vector2.Zero;
        velocity.Angular = 0;
        
        // Add body request - dynamic box
        ref var bodyRequest = ref _world.GetPool<CreateBodyRequest>().Add(entity);
        bodyRequest.BodyConfig = new BodyConfig
        {
            Type = BodyType.Dynamic,
            Mass = mass,
            Friction = 0.5f,
            Restitution = 0.2f,
            IsSensor = false,
            CategoryBits = PLATFORM_LAYER,
            MaskBits = PLAYER_LAYER | PLATFORM_LAYER
        };
        bodyRequest.ShapeConfig = ShapeConfig.Box(size);
        
        // Add box tag for identification
        _world.GetPool<PhysicsBox>().Add(entity);
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
