using System.Reflection;
using Karpik.Engine.Shared.ECS;

namespace Karpik.Engine.Shared;

internal class SystemExecutionNode
{
    public int Index { get; }
    public IEcsRunParallel System { get; }
    public HashSet<Type> ReadTypes { get; }
    public HashSet<Type> WriteTypes { get; }
    public bool IsAccessKnown { get; }
    public List<SystemExecutionNode> Dependencies { get; } = new();
    public int IncomingDependenciesCount { get; set; } // Счётчик для выполнения
    public int DependencyCount => Dependencies.Count;

    public SystemExecutionNode(int index, IEcsRunParallel system)
    {
        Index = index;
        System = system;
        (ReadTypes, WriteTypes, IsAccessKnown) = GetAspectTypes(system);
    }

    public void AddDependency(SystemExecutionNode other)
    {
        Dependencies.Add(other);
    }

    private (HashSet<Type>, HashSet<Type>, bool) GetAspectTypes(IEcsRunParallel system)
    {
        var readTypes = new HashSet<Type>();
        var writeTypes = new HashSet<Type>();
        bool isAccessKnown = false;

        var nested = system.GetType().GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
        var aspectDefinitions = nested.Where(t => t.IsAssignableTo(typeof(EcsAspect)));
        
        foreach (var definition in aspectDefinitions)
        {
            var pools = definition.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var pool in pools)
            {
                var type = pool.FieldType;
                bool isReadonly = !type.IsAssignableTo(typeof(IEcsPool))
                                  && type.IsAssignableTo(typeof(IEcsReadonlyPool));

                if (isReadonly)
                {
                    readTypes.Add(type.GetGenericArguments()[0]);
                    isAccessKnown = true;
                }
                else if (type.IsAssignableTo(typeof(IEcsPool)))
                {
                    writeTypes.Add(type.GetGenericArguments()[0]);
                    isAccessKnown = true;
                }
            }
        }

        return (readTypes, writeTypes, isAccessKnown);
    }

    public void Destroy()
    {
        ReadTypes.Clear();
        WriteTypes.Clear();
        Dependencies.Clear();
    }
}
