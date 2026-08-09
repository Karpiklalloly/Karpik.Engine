using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS;

namespace Karpik.Engine.Shared.Physics.Core;

public sealed class Physics2DBodyRestoreSystem(DefaultWorld world) : ISystemInit
{
    private class RuntimeBodyAspect : EcsAspect
    {
        public EcsPool<PhysicsBodyRef> BodyRefs = Inc;
        public ReadonlyEcsPool<PhysicsBodyDefinition> Definitions = Opt;
    }

    private class DefinitionAspect : EcsAspect
    {
        public ReadonlyEcsPool<PhysicsBodyDefinition> Definitions = Inc;
        public ReadonlyEcsPool<Transform2D> Transforms = Inc;
        public EcsPool<CreateBodyRequest> Requests = Opt;
        public EcsPool<PhysicsBodyRef> BodyRefs = Exc;
    }

    public void Init()
    {
        int clearedRuntimeRefs = 0;
        int missingDefinitions = 0;
        foreach (var entity in world.Where(out RuntimeBodyAspect runtimeBody))
        {
            if (!runtimeBody.Definitions.Has(entity))
            {
                missingDefinitions++;
            }

            runtimeBody.BodyRefs.Del(entity);
            clearedRuntimeRefs++;
        }

        int queuedBodies = 0;
        foreach (var entity in world.Where(out DefinitionAspect definition))
        {
            if (definition.Requests.Has(entity))
            {
                continue;
            }

            ref readonly var bodyDefinition = ref definition.Definitions.Get(entity);
            ref var request = ref definition.Requests.Add(entity);
            request.BodyConfig = bodyDefinition.BodyConfig;
            request.ShapeConfig = bodyDefinition.ShapeConfig;
            queuedBodies++;
        }

        if (clearedRuntimeRefs > 0 || queuedBodies > 0 || missingDefinitions > 0)
        {
            Console.WriteLine(
                $"[Physics2D] Cleared {clearedRuntimeRefs} runtime body refs, queued {queuedBodies} body restores, missing definitions {missingDefinitions}");
        }
    }
}
