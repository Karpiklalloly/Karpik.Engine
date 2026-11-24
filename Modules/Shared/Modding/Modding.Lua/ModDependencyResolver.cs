using System.Collections.Frozen;
using System.Collections.Immutable;
using Karpik.Content.Core;

namespace Karpik.Engine.Shared.Modding.Lua;

internal enum ModCookedAvailability { Absent, AdmittedCandidate, Rejected }

internal readonly record struct ModDependencyNode(string Id, string? Version, ModDependencyManifest Dependencies,
    bool HasScriptPayload, ModCookedAvailability CookedAvailability);

internal sealed record ModDependencyResolution(ImmutableArray<string> OrderedIds,
    FrozenDictionary<string, FrozenSet<string>> DirectTargetsById, FrozenSet<string> ParticipatingCookedOnlyProviderIds);

internal static class ModDependencyResolver
{
    public static ModDependencyResolution Resolve(IReadOnlyList<ModDependencyNode> nodes, AssetTarget side,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string sideName = side.ToCanonicalString();
        bool isClient = side == AssetTarget.Client;
        string sideScope = isClient ? "client" : "server";
        var states = new Dictionary<string, NodeState>(nodes.Count, StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!states.TryAdd(node.Id, new NodeState(node)))
                throw new ModDependencyException($"Duplicate mod id '{node.Id}' on {sideName}.");
        }

        var ready = new PriorityQueue<NodeState, string>(StringComparer.Ordinal);
        foreach (var source in states.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = source.Node.Dependencies;
            AddEdges(source, manifest.Shared, "shared");
            AddEdges(source, isClient ? manifest.Client : manifest.Server, sideScope);
            if (source.Remaining == 0)
                ready.Enqueue(source, source.Node.Id);
        }

        var ordered = ImmutableArray.CreateBuilder<string>(states.Count);
        while (ready.TryDequeue(out var target, out _))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ordered.Add(target.Node.Id);
            if (target.Dependents is not { } dependents) continue;
            foreach (var dependent in dependents)
            {
                if (--dependent.Remaining == 0)
                    ready.Enqueue(dependent, dependent.Node.Id);
            }
        }

        if (ordered.Count != states.Count)
            throw DescribeCycle(states, sideName, cancellationToken);

        var direct = states.Values.ToFrozenDictionary(static state => state.Node.Id,
            static state => state.DirectTargets?.ToFrozenSet(StringComparer.Ordinal) ?? FrozenSet<string>.Empty,
            StringComparer.Ordinal);
        var cooked = states.Values.Where(static state =>
                state.Node is { HasScriptPayload: false, CookedAvailability: ModCookedAvailability.AdmittedCandidate } &&
                (state.DirectTargets is not null || state.Dependents is not null))
            .Select(static state => state.Node.Id).ToFrozenSet(StringComparer.Ordinal);
        return new ModDependencyResolution(ordered.MoveToImmutable(), direct, cooked);

        void AddEdges(NodeState source, ImmutableArray<ModDependencyDeclaration> declarations, string scope)
        {
            foreach (var dependency in declarations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!states.TryGetValue(dependency.Id, out var target) ||
                    target.Node is { HasScriptPayload: false, CookedAvailability: ModCookedAvailability.Absent })
                {
                    if (dependency.Optional) continue;
                    throw new ModDependencyException($"{Context()} is required but no payload is available.");
                }
                if (target.Node is { HasScriptPayload: false, CookedAvailability: ModCookedAvailability.Rejected })
                    throw new ModDependencyException($"{Context()} has a rejected cooked package and no Lua payload.");

                if (dependency.MinVersion is not null || dependency.MaxVersionExclusive is not null)
                {
                    string? versionText = target.Node.Version;
                    Version? version = ModDependencyManifest.ParseNumericVersion(versionText);
                    if (version is null || (dependency.MinVersion is not null && version < dependency.MinVersion) ||
                        (dependency.MaxVersionExclusive is not null && version >= dependency.MaxVersionExclusive))
                    {
                        string range = $"minVersion={dependency.MinVersion?.ToString() ?? "unbounded"}, " +
                            $"maxVersionExclusive={dependency.MaxVersionExclusive?.ToString() ?? "unbounded"}";
                        string reason = version is null ? "; expected MAJOR.MINOR.PATCH" : string.Empty;
                        throw new ModDependencyException($"{Context()} has incompatible version '{versionText ?? "<missing>"}' ({range}{reason}).");
                    }
                }

                if ((source.DirectTargets ??= new HashSet<string>(StringComparer.Ordinal)).Add(dependency.Id))
                {
                    source.Remaining++;
                    (target.Dependents ??= []).Add(source);
                }

                string Context() => $"Mod '{source.Node.Id}' dependency '{dependency.Id}' on {sideName} ({scope})";
            }
        }
    }

    private static ModDependencyException DescribeCycle(Dictionary<string, NodeState> states, string sideName,
        CancellationToken cancellationToken)
    {
        var current = states.Values.Where(static state => state.Remaining > 0)
            .MinBy(static state => state.Node.Id, StringComparer.Ordinal)!;
        var path = new List<string>();
        var stepById = new Dictionary<string, int>(StringComparer.Ordinal);
        while (stepById.TryAdd(current.Node.Id, path.Count))
        {
            cancellationToken.ThrowIfCancellationRequested();
            path.Add(current.Node.Id);
            // After Kahn's pass every blocked node still has a blocked prerequisite.
            string prerequisite = current.DirectTargets!.Where(id => states[id].Remaining > 0).Min(StringComparer.Ordinal)!;
            current = states[prerequisite];
        }

        int cycleStart = stepById[current.Node.Id];
        path.Add(current.Node.Id);
        return new ModDependencyException($"Mod dependency cycle on {sideName}: {string.Join(" -> ", path.Skip(cycleStart))}.");
    }

    private sealed class NodeState(ModDependencyNode node)
    {
        public ModDependencyNode Node { get; } = node;
        public int Remaining;
        public HashSet<string>? DirectTargets;
        public List<NodeState>? Dependents;
    }
}
