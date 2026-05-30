using DCFApixels.DragonECS.RunnersCore;
using Karpik.Jobs;

namespace Karpik.Engine.Shared.ECS;

public interface IEcsRunParallel : IEcsProcess
{
    void RunParallel();
}

public class EcsRunParallelRunner : EcsRunner<IEcsRunParallel>, IEcsRunParallel, IEcsDestroy
{
    private SystemExecutionNode[] _executionNodes;
    private JobSystem _jobSystem;
    private JobHandle[] _jobHandles;
    private JobHandle[][] _dependencyHandleBuffers;

    public void RunParallel()
    {
        for (int nodeIndex = 0; nodeIndex < _executionNodes.Length; nodeIndex++)
        {
            var node = _executionNodes[nodeIndex];
            var dependencyHandles = _dependencyHandleBuffers[nodeIndex];
            for (int i = 0; i < node.Dependencies.Count; i++)
            {
                dependencyHandles[i] = _jobHandles[node.Dependencies[i].Index];
            }

            _jobHandles[nodeIndex].Dispose();
            _jobHandles[nodeIndex] = _jobSystem.Enqueue(node.System.RunParallel, dependencyHandles);
        }

        _jobSystem.WaitForCompletion();
    }

    private void BuildDependencyGraph(EcsProcess<IEcsRunParallel> process)
    {
        _executionNodes = process
            .Select(static (system, index) => new SystemExecutionNode(index, system))
            .ToArray();

        for (int i = 0; i < _executionNodes.Length; i++)
        {
            var nodeA = _executionNodes[i];

            for (int j = i + 1; j < _executionNodes.Length; j++)
            {
                var nodeB = _executionNodes[j];
                if (HasDirectedConflict(nodeA, nodeB))
                {
                    nodeB.AddDependency(nodeA);
                }
            }
        }

        _jobHandles = new JobHandle[_executionNodes.Length];
        _dependencyHandleBuffers = new JobHandle[_executionNodes.Length][];
        for (int i = 0; i < _executionNodes.Length; i++)
        {
            _dependencyHandleBuffers[i] = new JobHandle[_executionNodes[i].DependencyCount];
        }

        foreach (var node in _executionNodes)
        {
            Console.WriteLine($"{node.System.GetType().Name} deps ({node.DependencyCount}) : " +
                              $"{string.Join(", ", node.Dependencies.Select(d => d.System.GetType().Name))}");
        }
    }
    
    private bool HasDirectedConflict(SystemExecutionNode previousNode, SystemExecutionNode subsequentNode)
    {
        if (!previousNode.IsAccessKnown || !subsequentNode.IsAccessKnown)
        {
            return true;
        }

        var writesOfPrevious = previousNode.WriteTypes;
    
        var subsequentReadsOrWrites = new HashSet<Type>(subsequentNode.ReadTypes);
        subsequentReadsOrWrites.UnionWith(subsequentNode.WriteTypes);

        if (writesOfPrevious.Overlaps(subsequentReadsOrWrites))
        {
            return true;
        }

        return previousNode.ReadTypes.Overlaps(subsequentNode.WriteTypes);
    }

    public void Init()
    {
        BuildDependencyGraph(Process);
        _jobSystem = new JobSystem(Environment.ProcessorCount, "ParallelRunner");
    }

    public void Destroy()
    {
        _jobSystem.Shutdown();
        foreach (var item in _executionNodes)
        {
            item.Destroy();
        }
        Array.Resize(ref _executionNodes, 0);

        foreach (var handle in _jobHandles)
        {
            handle.Dispose();
        }
        Array.Resize(ref _jobHandles, 0);
        Array.Resize(ref _dependencyHandleBuffers, 0);
    }
}
