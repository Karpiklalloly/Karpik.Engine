using System.Numerics;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.Shared.Physics.Aether2D;

public static class SystemAetherConverters
{
    extension(Vector2 vector)
    {
        public nkast.Aether.Physics2D.Common.Vector2 Aether => new(vector.X, vector.Y);
    }
    
    extension(nkast.Aether.Physics2D.Common.Vector2 vector)
    {
        public Vector2 Numeric => new(vector.X, vector.Y);
    }
}

public static class CoreAetherConverters
{
    extension(BodyType bodyType)
    {
        public nkast.Aether.Physics2D.Dynamics.BodyType Aether
        {
            get
            {
                return bodyType switch
                {
                    BodyType.Static => nkast.Aether.Physics2D.Dynamics.BodyType.Static,
                    BodyType.Kinematic => nkast.Aether.Physics2D.Dynamics.BodyType.Kinematic,
                    BodyType.Dynamic => nkast.Aether.Physics2D.Dynamics.BodyType.Dynamic,
                    _ => throw new ArgumentOutOfRangeException(nameof(bodyType), bodyType, null)
                };
            }
        }
    }
    
    extension(nkast.Aether.Physics2D.Dynamics.BodyType bodyType)
    {
        public BodyType Core
        {
            get
            {
                return bodyType switch
                {
                    nkast.Aether.Physics2D.Dynamics.BodyType.Static => BodyType.Static,
                    nkast.Aether.Physics2D.Dynamics.BodyType.Kinematic => BodyType.Kinematic,
                    nkast.Aether.Physics2D.Dynamics.BodyType.Dynamic => BodyType.Dynamic,
                    _ => throw new ArgumentOutOfRangeException(nameof(bodyType), bodyType, null)
                };
            }
        }
    }
}