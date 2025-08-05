using System.Numerics;
using Game.Generated.Client;
using Karpik.Engine.Shared;
using Karpik.Engine.Shared.DEMO;
using Network;
using Raylib_cs;

namespace Karpik.Engine.Client;

public class InputSystem : IEcsRun, IEcsInject<EcsDefaultWorld>
{
    class Aspect : EcsAspect
    {
        public EcsPool<Position> position = Inc;
        public EcsPool<NetworkId> networkId = Inc;
        public EcsPool<LocalPlayer> player = Inc;
    }
    
    private EcsDefaultWorld _world;
    
    public void Run()
    {
        Vector2 currentInput = Vector2.Zero;

        if (Input.IsDown(KeyboardKey.A) || Input.IsDown(KeyboardKey.Left))
        {
            currentInput.X -= 1;
        }

        if (Input.IsDown(KeyboardKey.D) || Input.IsDown(KeyboardKey.Right))
        {
            currentInput.X += 1;
        }
        
        if (Input.IsDown(KeyboardKey.W) || Input.IsDown(KeyboardKey.Up))
        {
            currentInput.Y += 1;
        }
        
        if (Input.IsDown(KeyboardKey.S) || Input.IsDown(KeyboardKey.Down))
        {
            currentInput.Y -= 1;
        }

        if (Input.IsPressed(KeyboardKey.Space))
        {
            currentInput.Y += 1;
        }

        if (currentInput.LengthSquared() > 0.001f)
        {
            foreach (var e in _world.Where(out Aspect a))
            {
                var netId = a.networkId.Get(e).Id;
                Rpc.Instance.Move(new MoveCommand()
                {
                    Direction = currentInput,
                    Source = netId,
                    Target = netId
                });
            }
        }
    }

    public void Inject(EcsDefaultWorld obj)
    {
        _world = obj;
    }
}