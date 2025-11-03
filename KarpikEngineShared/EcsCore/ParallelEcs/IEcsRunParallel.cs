using DCFApixels.DragonECS.RunnersCore;
using Karpik.Jobs;

namespace Karpik.Engine.Shared;

public interface IEcsRunParallel : IEcsProcess
{
    void RunParallel();
}

public class EcsRunParallelRunner : EcsRunner<IEcsRunParallel>, IEcsRunParallel
{
    private SystemExecutionNode[] _executionNodes;
    private JobSystem _jobSystem;
    private readonly Dictionary<SystemExecutionNode, JobHandle> _jobHandles = new();

    public void RunParallel()
    {
        _jobHandles.Clear();

        foreach (var node in _executionNodes)
        {
            var dependencyHandles = new JobHandle[node.Dependencies.Count];
            for (int i = 0; i < node.Dependencies.Count; i++)
            {
                dependencyHandles[i] = _jobHandles[node.Dependencies[i]];
            }
            
            var handle = _jobSystem.Enqueue(node.System.RunParallel, dependencyHandles);

            _jobHandles.Add(node, handle);
        }

        _jobSystem.WaitForCompletion();
    }

    private void BuildDependencyGraph(EcsProcess<IEcsRunParallel> process)
    {
        _executionNodes = process.Select(static system => new SystemExecutionNode(system)).ToArray();

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

        foreach (var node in _executionNodes)
        {
            Console.WriteLine($"{node.System.GetType().Name} deps ({node.DependencyCount}) : " +
                              $"{string.Join(", ", node.Dependencies.Select(d => d.System.GetType().Name))}");
        }
    }
    
    private bool HasDirectedConflict(SystemExecutionNode previousNode, SystemExecutionNode subsequentNode)
    {
        var writesOfPrevious = previousNode.WriteTypes;
    
        var subsequentReadsOrWrites = new HashSet<Type>(subsequentNode.ReadTypes);
        subsequentReadsOrWrites.UnionWith(subsequentNode.WriteTypes);

        if (writesOfPrevious.Overlaps(subsequentReadsOrWrites))
        {
            return true;
        }

        return false;
    }

    public void Init()
    {
        BuildDependencyGraph(Process);
        _jobSystem = new JobSystem(Environment.ProcessorCount);
    }
}