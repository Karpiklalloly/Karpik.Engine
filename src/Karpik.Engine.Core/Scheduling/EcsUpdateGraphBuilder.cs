namespace Karpik.Engine.Shared.ECS.Scheduling;

public static class EcsUpdateGraphBuilder
{
    public static EcsUpdateGraph BuildInstances(ReadOnlySpan<EcsUpdateSystemDescriptor> descriptors)
    {
        if (descriptors.IsEmpty) return EcsUpdateGraph.Empty;

        var instanceIds = new Dictionary<string, int>(descriptors.Length, StringComparer.Ordinal);
        var systemIds = new Dictionary<Type, int>(descriptors.Length);
        var systemTypes = new Type[descriptors.Length];
        for (int i = 0; i < descriptors.Length; i++)
        {
            EcsUpdateSystemDescriptor descriptor = descriptors[i];
            if (string.IsNullOrWhiteSpace(descriptor.SystemId))
                throw new EcsUpdateGraphBuildException("Instance descriptor requires a nonempty system ID.");
            if (descriptor.SystemType is null)
                throw new EcsUpdateGraphBuildException($"System '{descriptor.SystemId}' requires a system type.");
            if (!instanceIds.TryAdd(descriptor.SystemId, i))
                throw new EcsUpdateGraphBuildException($"Duplicate system ID '{descriptor.SystemId}'.");

            systemTypes[i] = descriptor.SystemType;
            if (!systemIds.TryAdd(descriptor.SystemType, i)) systemIds[descriptor.SystemType] = -1;
        }

        Type[] componentTypes = BuildComponentTypes(descriptors);
        Dictionary<Type, int> componentIds = BuildComponentIds(componentTypes);
        SystemAccess[] accesses = BuildSystemAccess(descriptors, componentIds, componentTypes.Length);
        bool[] dependencyBits = BuildDependencyBits(descriptors, accesses, systemIds, instanceIds);
        return new EcsUpdateGraph(systemTypes, componentTypes,
            BuildNodes(accesses, dependencyBits, descriptors.Length),
            BuildExecutionOrder(dependencyBits, descriptors.Length));
    }

    public static EcsUpdateGraph Build(
        ReadOnlySpan<Type> registeredSystemTypes,
        ReadOnlySpan<EcsUpdateSystemDescriptor> descriptors)
    {
        if (registeredSystemTypes.Length == 0)
        {
            return new EcsUpdateGraph(
                Array.Empty<Type>(),
                Array.Empty<Type>(),
                Array.Empty<EcsUpdateGraphNode>(),
                Array.Empty<int>());
        }

        var descriptorByType = new Dictionary<Type, EcsUpdateSystemDescriptor>(descriptors.Length);
        for (int i = 0; i < descriptors.Length; i++)
        {
            EcsUpdateSystemDescriptor descriptor = descriptors[i];
            if (!descriptorByType.TryAdd(descriptor.SystemType, descriptor))
            {
                throw new EcsUpdateGraphBuildException(
                    $"Duplicate ECS update registry descriptor for system '{descriptor.SystemType.FullName}'.");
            }
        }

        var systemTypes = new Type[registeredSystemTypes.Length];
        var systemIds = new Dictionary<Type, int>(registeredSystemTypes.Length);
        var orderedDescriptors = new EcsUpdateSystemDescriptor[registeredSystemTypes.Length];

        for (int i = 0; i < registeredSystemTypes.Length; i++)
        {
            Type systemType = registeredSystemTypes[i];
            if (!systemIds.TryAdd(systemType, i))
            {
                throw new EcsUpdateGraphBuildException(
                    $"Duplicate registered ECS update system '{systemType.FullName}'.");
            }

            if (!descriptorByType.TryGetValue(systemType, out EcsUpdateSystemDescriptor descriptor))
            {
                throw new EcsUpdateGraphBuildException(
                    $"Missing generated ECS update registry descriptor for registered system '{systemType.FullName}'.");
            }

            systemTypes[i] = systemType;
            orderedDescriptors[i] = descriptor;
        }

        Type[] componentTypes = BuildComponentTypes(orderedDescriptors);
        Dictionary<Type, int> componentIds = BuildComponentIds(componentTypes);
        SystemAccess[] accesses = BuildSystemAccess(orderedDescriptors, componentIds, componentTypes.Length);
        bool[] dependencyBits = BuildDependencyBits(orderedDescriptors, accesses, systemIds);
        EcsUpdateGraphNode[] nodes = BuildNodes(accesses, dependencyBits, registeredSystemTypes.Length);
        int[] executionOrder = BuildExecutionOrder(dependencyBits, registeredSystemTypes.Length);

        return new EcsUpdateGraph(systemTypes, componentTypes, nodes, executionOrder);
    }

    private static Type[] BuildComponentTypes(ReadOnlySpan<EcsUpdateSystemDescriptor> descriptors)
    {
        var componentTypes = new HashSet<Type>();
        for (int i = 0; i < descriptors.Length; i++)
        {
            ReadOnlySpan<EcsComponentAccessDescriptor> accesses = descriptors[i].Accesses.Span;
            for (int j = 0; j < accesses.Length; j++)
            {
                componentTypes.Add(accesses[j].ComponentType);
            }
        }

        Type[] result = componentTypes.ToArray();
        Array.Sort(result, TypeMetadataComparer.Instance);
        return result;
    }

    private static Dictionary<Type, int> BuildComponentIds(Type[] componentTypes)
    {
        var componentIds = new Dictionary<Type, int>(componentTypes.Length);
        for (int i = 0; i < componentTypes.Length; i++)
        {
            componentIds.Add(componentTypes[i], i);
        }

        return componentIds;
    }

    private static SystemAccess[] BuildSystemAccess(
        ReadOnlySpan<EcsUpdateSystemDescriptor> descriptors,
        Dictionary<Type, int> componentIds,
        int componentCount)
    {
        var accesses = new SystemAccess[descriptors.Length];
        var reads = new bool[componentCount];
        var writes = new bool[componentCount];

        for (int i = 0; i < descriptors.Length; i++)
        {
            Array.Clear(reads);
            Array.Clear(writes);

            ReadOnlySpan<EcsComponentAccessDescriptor> descriptorAccesses = descriptors[i].Accesses.Span;
            for (int j = 0; j < descriptorAccesses.Length; j++)
            {
                EcsComponentAccessDescriptor access = descriptorAccesses[j];
                int componentId = componentIds[access.ComponentType];
                switch (access.Mode)
                {
                    case EcsAccessMode.Write:
                        writes[componentId] = true;
                        reads[componentId] = false;
                        break;
                    case EcsAccessMode.Read when !writes[componentId]:
                        reads[componentId] = true;
                        break;
                    case EcsAccessMode.Read:
                        break;
                    default:
                        throw new EcsUpdateGraphBuildException(
                            $"Unsupported ECS update access mode '{access.Mode}' for component '{access.ComponentType.FullName}'.");
                }
            }

            accesses[i] = new SystemAccess(
                descriptors[i].IsSequential,
                ToSortedIds(reads),
                ToSortedIds(writes));
        }

        return accesses;
    }

    private static int[] ToSortedIds(bool[] flags)
    {
        int count = 0;
        for (int i = 0; i < flags.Length; i++)
        {
            if (flags[i])
            {
                count++;
            }
        }

        if (count == 0)
            return Array.Empty<int>();

        var ids = new int[count];
        int target = 0;
        for (int i = 0; i < flags.Length; i++)
        {
            if (flags[i])
            {
                ids[target++] = i;
            }
        }

        return ids;
    }

    private static bool[] BuildDependencyBits(
        ReadOnlySpan<EcsUpdateSystemDescriptor> descriptors,
        SystemAccess[] accesses,
        Dictionary<Type, int> systemIds,
        Dictionary<string, int>? instanceIds = null)
    {
        int systemCount = descriptors.Length;
        var dependencyBits = new bool[systemCount * systemCount];

        if (instanceIds is null) AddAccessDependencies(accesses, dependencyBits, default);

        for (int systemId = 0; systemId < systemCount; systemId++)
        {
            ReadOnlySpan<EcsSystemOrderDescriptor> orders = descriptors[systemId].Orders.Span;
            for (int i = 0; i < orders.Length; i++)
            {
                EcsSystemOrderDescriptor order = orders[i];
                int targetSystemId = ResolveOrderTarget(order, systemIds, instanceIds);

                if (order.Kind == EcsOrderKind.After)
                {
                    SetDependency(dependencyBits, systemCount, systemId, targetSystemId);
                }
                else if (order.Kind == EcsOrderKind.Before)
                {
                    SetDependency(dependencyBits, systemCount, targetSystemId, systemId);
                }
                else
                {
                    throw new EcsUpdateGraphBuildException(
                        $"Unsupported ECS update order kind '{order.Kind}' for target '{order.TargetSystemId ?? order.TargetSystemType?.FullName}'.");
                }
            }
        }

        if (instanceIds is not null)
        {
            // Orient barriers along explicit order so a backwards ID dependency cannot create a false cycle.
            AddAccessDependencies(accesses, dependencyBits, BuildExecutionOrder(dependencyBits, systemCount));
        }
        return dependencyBits;
    }

    private static int ResolveOrderTarget(EcsSystemOrderDescriptor order,
        Dictionary<Type, int> systemIds, Dictionary<string, int>? instanceIds)
    {
        if (order.TargetSystemId is string id)
        {
            if (instanceIds is not null && instanceIds.TryGetValue(id, out int instanceIndex))
                return instanceIndex;
            throw new EcsUpdateGraphBuildException($"ECS update order target ID '{id}' is not registered in an instance graph.");
        }

        if (order.TargetSystemType is not Type type)
            throw new EcsUpdateGraphBuildException("ECS update order requires a target type or instance ID.");
        if (!systemIds.TryGetValue(type, out int index))
            throw new EcsUpdateGraphBuildException($"ECS update order target '{type.FullName}' is not registered.");
        if (index < 0)
            throw new EcsUpdateGraphBuildException($"ECS update order target type '{type.FullName}' is ambiguous; use an instance ID.");
        return index;
    }

    private static void AddAccessDependencies(SystemAccess[] accesses, bool[] dependencyBits, ReadOnlySpan<int> order)
    {
        for (int previous = 0; previous < accesses.Length; previous++)
        {
            int left = order.IsEmpty ? previous : order[previous];
            for (int subsequent = previous + 1; subsequent < accesses.Length; subsequent++)
            {
                int right = order.IsEmpty ? subsequent : order[subsequent];
                if (accesses[left].IsSequential || accesses[right].IsSequential || HasConflict(accesses[left], accesses[right]))
                    SetDependency(dependencyBits, accesses.Length, right, left);
            }
        }
    }

    private static bool HasConflict(SystemAccess previous, SystemAccess subsequent)
    {
        return Overlaps(previous.WriteComponentIds, subsequent.ReadComponentIds) ||
               Overlaps(previous.WriteComponentIds, subsequent.WriteComponentIds) ||
               Overlaps(previous.ReadComponentIds, subsequent.WriteComponentIds);
    }

    private static bool Overlaps(int[] left, int[] right)
    {
        int leftIndex = 0;
        int rightIndex = 0;

        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            int leftValue = left[leftIndex];
            int rightValue = right[rightIndex];
            if (leftValue == rightValue)
                return true;

            if (leftValue < rightValue)
            {
                leftIndex++;
            }
            else
            {
                rightIndex++;
            }
        }

        return false;
    }

    private static EcsUpdateGraphNode[] BuildNodes(
        SystemAccess[] accesses,
        bool[] dependencyBits,
        int systemCount)
    {
        var nodes = new EcsUpdateGraphNode[systemCount];
        for (int systemId = 0; systemId < systemCount; systemId++)
        {
            nodes[systemId] = new EcsUpdateGraphNode(
                systemId,
                accesses[systemId].IsSequential,
                accesses[systemId].ReadComponentIds,
                accesses[systemId].WriteComponentIds,
                BuildDependencyIds(dependencyBits, systemCount, systemId));
        }

        return nodes;
    }

    private static int[] BuildDependencyIds(bool[] dependencyBits, int systemCount, int systemId)
    {
        int count = 0;
        for (int dependencyId = 0; dependencyId < systemCount; dependencyId++)
        {
            if (HasDependency(dependencyBits, systemCount, systemId, dependencyId))
            {
                count++;
            }
        }

        if (count == 0)
            return Array.Empty<int>();

        var dependencies = new int[count];
        int target = 0;
        for (int dependencyId = 0; dependencyId < systemCount; dependencyId++)
        {
            if (HasDependency(dependencyBits, systemCount, systemId, dependencyId))
            {
                dependencies[target++] = dependencyId;
            }
        }

        return dependencies;
    }

    private static int[] BuildExecutionOrder(bool[] dependencyBits, int systemCount)
    {
        var incomingCounts = new int[systemCount];
        for (int systemId = 0; systemId < systemCount; systemId++)
        {
            for (int dependencyId = 0; dependencyId < systemCount; dependencyId++)
            {
                if (HasDependency(dependencyBits, systemCount, systemId, dependencyId))
                {
                    incomingCounts[systemId]++;
                }
            }
        }

        var emitted = new bool[systemCount];
        var executionOrder = new int[systemCount];

        for (int step = 0; step < systemCount; step++)
        {
            int selected = -1;
            for (int systemId = 0; systemId < systemCount; systemId++)
            {
                if (!emitted[systemId] && incomingCounts[systemId] == 0)
                {
                    selected = systemId;
                    break;
                }
            }

            if (selected < 0)
            {
                throw new EcsUpdateGraphBuildException("ECS update graph contains an explicit order cycle.");
            }

            emitted[selected] = true;
            executionOrder[step] = selected;

            for (int dependentId = 0; dependentId < systemCount; dependentId++)
            {
                if (HasDependency(dependencyBits, systemCount, dependentId, selected))
                {
                    incomingCounts[dependentId]--;
                }
            }
        }

        return executionOrder;
    }

    private static void SetDependency(bool[] dependencyBits, int systemCount, int dependentId, int dependencyId)
    {
        if (dependentId == dependencyId)
        {
            throw new EcsUpdateGraphBuildException("ECS update graph contains an explicit order cycle.");
        }

        dependencyBits[(dependentId * systemCount) + dependencyId] = true;
    }

    private static bool HasDependency(bool[] dependencyBits, int systemCount, int dependentId, int dependencyId)
    {
        return dependencyBits[(dependentId * systemCount) + dependencyId];
    }

    private readonly struct SystemAccess
    {
        public SystemAccess(bool isSequential, int[] readComponentIds, int[] writeComponentIds)
        {
            IsSequential = isSequential;
            ReadComponentIds = readComponentIds;
            WriteComponentIds = writeComponentIds;
        }

        public bool IsSequential { get; }
        public int[] ReadComponentIds { get; }
        public int[] WriteComponentIds { get; }
    }

    private sealed class TypeMetadataComparer : IComparer<Type>
    {
        public static readonly TypeMetadataComparer Instance = new();

        public int Compare(Type? x, Type? y)
        {
            if (ReferenceEquals(x, y))
                return 0;

            if (x is null)
                return -1;

            if (y is null)
                return 1;

            int nameCompare = string.CompareOrdinal(x.FullName, y.FullName);
            if (nameCompare != 0)
                return nameCompare;

            return string.CompareOrdinal(x.Assembly.FullName, y.Assembly.FullName);
        }
    }
}
