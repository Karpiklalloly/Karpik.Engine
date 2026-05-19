using System.Numerics;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.MyGame.Server.Main.Systems;

public class KinematicControllerSystem : ISystemFixedUpdate
{
    private const float HALF_HEIGHT = 1.0f;
    private const float SKIN_WIDTH = 1.0f;
    
    class Aspect : EcsAspect
    {
        public EcsPool<PhysicsBodyRef> body = Inc;
        public EcsPool<Transform2D> transform = Inc;
        public EcsPool<KinematicCharacterController> controller = Inc;
        public EcsPool<WannaMove> move = Inc;
    }
    
    [DI] private EcsDefaultWorld _world = null!;
    [DI] private IPhysicsWorld2D _physicsWorld2D = null!;
    [DI] private Time _time = null!;
    
    private RaycastHit2D[] _hits = new RaycastHit2D[16];
    
    public void FixedUpdate()
    {
        foreach (var e in _world.Where(out Aspect a))
        {
            ref var controller = ref a.controller.Get(e);
            ref var bodyRef = ref a.body.Get(e);
            ref var transform = ref a.transform.Get(e);
            ref var move = ref a.move.Get(e);

            Vector2 targetVelocity = _physicsWorld2D.GetVelocity(bodyRef.Handle);

            CheckGround(ref controller, ref bodyRef, ref transform);
            ApplyHorizontalInput(ref controller, ref move, ref targetVelocity);
            ApplyGravity(ref controller, ref targetVelocity);
            ApplyJump(ref controller, ref move, ref targetVelocity);
            
            if (controller.IsCeiled && targetVelocity.Y > 0)
            {
                targetVelocity.Y = 0;
            }

            if (controller.TouchLeft && targetVelocity.X < 0)
            {
                targetVelocity.X = 0;
            }

            if (controller.TouchRight && targetVelocity.X > 0)
            {
                targetVelocity.X = 0;
            }

            _physicsWorld2D.SetVelocity(bodyRef.Handle, targetVelocity);

            move.MoveX = 0;
            move.Jump = false;
        }
    }

    private void CheckGround(ref KinematicCharacterController controller, ref PhysicsBodyRef bodyRef, ref Transform2D transform)
    {
        controller.IsGrounded = false;
        controller.IsCeiled = false;
        controller.TouchLeft = false;
        controller.TouchRight = false;
        controller.GroundNormal = Vector2.UnitY;
        Vector2 position = transform.Position;

        Vector2 leftHead = position + new Vector2(-SKIN_WIDTH / 2, HALF_HEIGHT);
        Vector2 rightHead = position + new Vector2(SKIN_WIDTH / 2, HALF_HEIGHT);
        CastCeilRay(leftHead, ref bodyRef, ref controller, _hits);
        CastCeilRay(rightHead, ref bodyRef, ref controller, _hits);
        
        Vector2 leftFoot = position + new Vector2(-SKIN_WIDTH / 2, -HALF_HEIGHT);
        Vector2 rightFoot = position + new Vector2(SKIN_WIDTH / 2, -HALF_HEIGHT);
        CastGroundRay(leftFoot, ref bodyRef, ref controller, _hits);
        CastGroundRay(rightFoot, ref bodyRef, ref controller, _hits);
        
        CastWallRay(position + new Vector2(0, HALF_HEIGHT), ref bodyRef, ref controller, _hits, new Vector2(SKIN_WIDTH / 2, 0));
        CastWallRay(position + new Vector2(0, 0), ref bodyRef, ref controller, _hits, new Vector2(SKIN_WIDTH / 2, 0));
        CastWallRay(position + new Vector2(0, -HALF_HEIGHT), ref bodyRef, ref controller, _hits, new Vector2(SKIN_WIDTH / 2, 0));
        
        CastWallRay(position + new Vector2(0, HALF_HEIGHT), ref bodyRef, ref controller, _hits, new Vector2(-SKIN_WIDTH / 2, 0));
        CastWallRay(position + new Vector2(0, 0), ref bodyRef, ref controller, _hits, new Vector2(-SKIN_WIDTH / 2, 0));
        CastWallRay(position + new Vector2(0, -HALF_HEIGHT), ref bodyRef, ref controller, _hits, new Vector2(-SKIN_WIDTH / 2, 0));
    }

    private void CastGroundRay(Vector2 from, ref PhysicsBodyRef bodyRef, ref KinematicCharacterController controller, Span<RaycastHit2D> hits)
    {
        Vector2 to = from + new Vector2(0, -KinematicCharacterController.GROUND_CHECK_DISTANCE);
        int hitsCount = _physicsWorld2D.Raycast(from, to, Physics2DLayers.Platform, hits);
        for (int i = 0; i < hitsCount; i++)
        {
            RaycastHit2D hit = _hits[i];
            if (hit.Body.Equals(bodyRef.Handle))
            {
                continue;
            }

            float angle = MathF.Acos(Math.Clamp(Vector2.Dot(hit.Normal, Vector2.UnitY), -1, 1));
            if (angle <= controller.MaxGroundAngle)
            {
                controller.IsGrounded = true;
                controller.GroundNormal = hit.Normal;
                return;
            }
        }
    }

    private void CastCeilRay(Vector2 from, ref PhysicsBodyRef bodyRef, ref KinematicCharacterController controller, Span<RaycastHit2D> hits)
    {
        Vector2 to = from + new Vector2(0, KinematicCharacterController.GROUND_CHECK_DISTANCE);
        int hitsCount = _physicsWorld2D.Raycast(from, to, Physics2DLayers.Platform, hits);
        for (int i = 0; i < hitsCount; i++)
        {
            RaycastHit2D hit = _hits[i];
            if (hit.Body.Equals(bodyRef.Handle))
            {
                continue;
            }

            float angle = MathF.Acos(Math.Clamp(Vector2.Dot(hit.Normal, -Vector2.UnitY), -1, 1));
            if (angle <= controller.MaxGroundAngle)
            {
                controller.IsCeiled = true;
                return;
            }
        }
    }
    
    private void CastWallRay(Vector2 from, ref PhysicsBodyRef bodyRef, ref KinematicCharacterController controller, Span<RaycastHit2D> hits, Vector2 direction)
    {
        Vector2 to = from + direction + new Vector2(MathF.Sign(direction.X) * KinematicCharacterController.GROUND_CHECK_DISTANCE, 0);
        int hitsCount = _physicsWorld2D.Raycast(from, to, Physics2DLayers.Platform, hits);
        for (int i = 0; i < hitsCount; i++)
        {
            RaycastHit2D hit = _hits[i];
            if (hit.Body.Equals(bodyRef.Handle))
            {
                continue;
            }

            if (direction.X > 0f && hit.Normal.X < -0.7f)
            {
                controller.TouchRight = true;
            }
            
            if (direction.X < 0f && hit.Normal.X > 0.7f)
            {
                controller.TouchLeft = true;
            }
        }
    }
    
    private void ApplyHorizontalInput(ref KinematicCharacterController controller, ref WannaMove move, ref Vector2 targetVelocity)
    {
        if (controller.IsGrounded)
        {
            Vector2 tangent = new Vector2(controller.GroundNormal.Y, -controller.GroundNormal.X);
            targetVelocity.X = tangent.X * move.MoveX * controller.MoveSpeed;
        }
        else if (controller.IsCeiled)
        {
            Vector2 tangent = new Vector2(controller.GroundNormal.Y, -controller.GroundNormal.X);
            targetVelocity.X = tangent.X * move.MoveX * controller.MoveSpeed;
        }
        else
        {
            targetVelocity.X = move.MoveX * controller.MoveSpeed;
        }
    }

    private void ApplyGravity(ref KinematicCharacterController controller, ref Vector2 targetVelocity)
    {
        if (controller.IsGrounded)
        {
            targetVelocity.Y = 0;
            return;
        }

        targetVelocity.Y += controller.Gravity * (float)_time.FixedDeltaTime;
        targetVelocity.Y = MathF.Max(targetVelocity.Y, -controller.MaxFallSpeed);
    }

    private void ApplyJump(ref KinematicCharacterController controller, ref WannaMove move, ref Vector2 targetVelocity)
    {
        if (move.Jump && controller.IsGrounded)
        {
            targetVelocity.Y = controller.JumpSpeed;
            controller.IsGrounded = false;
        }
    }
}
