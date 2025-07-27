using Box2D.NET.Bindings;

namespace Karpik.Engine.Server;

public struct ServerProgram : IDisposable
{
    private B2.WorldDef _worldDef;
    private B2.WorldId _physicsWorld;

    public ServerProgram()
    {
        _worldDef = new B2.WorldDef
        {
            gravity = new B2.Vec2
            {
                x = 0f,
                y = 0f
            }
        };
        unsafe
        {
            fixed (B2.WorldDef* worldDefPtr = &_worldDef)
            {
                //_physicsWorld = B2.CreateWorld(worldDefPtr);
            }
        }
    }

    public void Dispose()
    {
        B2.DestroyWorld(_physicsWorld);
    }
}