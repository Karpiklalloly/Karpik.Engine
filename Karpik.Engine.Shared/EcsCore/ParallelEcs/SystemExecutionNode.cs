using System.Reflection;

namespace Karpik.Engine.Shared;

public class SystemExecutionNode
{
    public IEcsRunParallel System { get; }
    public HashSet<Type> ReadTypes { get; }
    public HashSet<Type> WriteTypes { get; }
    public List<SystemExecutionNode> Dependencies { get; } = new();
    public int IncomingDependenciesCount { get; set; } // Счётчик для выполнения
    public int DependencyCount => Dependencies.Count;

    public SystemExecutionNode(IEcsRunParallel system)
    {
        System = system;
        (ReadTypes, WriteTypes) = GetAspectTypes(system);
    }

    public void AddDependency(SystemExecutionNode other)
    {
        Dependencies.Add(other);
    }

    private (HashSet<Type>, HashSet<Type>) GetAspectTypes(IEcsRunParallel system)
    {
        var readTypes = new HashSet<Type>();
        var writeTypes = new HashSet<Type>();

        var nested = system.GetType().GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
        var aspectDefinitions = nested.Where(t => t.IsAssignableTo(typeof(EcsAspect)));
        
        int i = 0;
        foreach (var definition in aspectDefinitions)
        {
            var pools = definition.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var pool in pools)
            {
                var type = pool.FieldType;
                bool isReadonly = !type.IsAssignableTo(typeof(IEcsPool))
                                  && type.IsAssignableTo(typeof(IEcsReadonlyPool));

                if (isReadonly) readTypes.Add(type.GetGenericArguments()[0]);
                else writeTypes.Add(type.GetGenericArguments()[0]);
            }
        }

        return (readTypes, writeTypes);
    }
}