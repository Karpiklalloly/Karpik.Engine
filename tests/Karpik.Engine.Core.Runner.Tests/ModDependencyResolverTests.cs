using Karpik.Content.Core;
using Karpik.Engine.Shared.Modding.Lua;
using Newtonsoft.Json.Linq;
using Xunit;

public sealed class ModDependencyResolverTests
{
    [Fact]
    public void NoEdgesPreservePreviousOrdinalOrderAndLegacyVersions()
    {
        ModDependencyNode[] nodes = [Node("b", version: "not a numeric version"), Node("z", hasScript: false), Node("A"), Node("a")];
        var result = ModDependencyResolver.Resolve(nodes, AssetTarget.Server);
        nodes[0] = Node("changed");

        Assert.Equal<string>(["A", "a", "b", "z"], result.OrderedIds);
        Assert.Equal(4, result.DirectTargetsById.Count);
        Assert.All(result.OrderedIds, id => Assert.Empty(result.DirectTargetsById[id]));
        Assert.Empty(result.ParticipatingCookedOnlyProviderIds);
        Assert.Empty(ModDependencyResolver.Resolve([], AssetTarget.Server).OrderedIds);
    }

    [Fact]
    public void RejectsDuplicateIdsOutsideDependencyGraph()
    {
        ModDependencyNode[] nodes = [Node("duplicate"), Node("duplicate", hasScript: false)];

        var error = Assert.Throws<ModDependencyException>(() => ModDependencyResolver.Resolve(nodes, AssetTarget.Client));

        Assert.Contains("Duplicate mod id 'duplicate'", error.Message);
        Assert.Contains("Client", error.Message);
    }

    [Theory]
    [InlineData(AssetTarget.Client, "b-client")]
    [InlineData(AssetTarget.Server, "c-server")]
    public void ResolvesSharedAndCurrentSideOnlyAndReturnsExactDirectTargets(AssetTarget side, string sideTarget)
    {
        ModDependencyNode[] nodes =
        [
            Node("a-source", """
                {
                  "shared":[{"id":"z-shared"},{"id":"optional-missing","optional":true},{"id":"Z-shared","optional":true}],
                  "client":[{"id":"z-shared"},{"id":"b-client"}],
                  "server":[{"id":"z-shared"},{"id":"c-server"}]
                }
                """),
            Node(sideTarget),
            Node("z-shared", """{"shared":[{"id":"y-transitive"}]}""", version: "arbitrary legacy"),
            Node("y-transitive")
        ];

        var result = ModDependencyResolver.Resolve(nodes, side);

        Assert.Equal<string>([sideTarget, "y-transitive", "z-shared", "a-source"], result.OrderedIds);
        Assert.True(result.DirectTargetsById["a-source"].SetEquals([sideTarget, "z-shared"]));
        Assert.False(result.DirectTargetsById.ContainsKey("A-source"));
        Assert.DoesNotContain("Z-shared", (IReadOnlySet<string>)result.DirectTargetsById["a-source"]);
        Assert.True(result.DirectTargetsById["z-shared"].SetEquals(["y-transitive"]));
    }

    [Theory]
    [InlineData("""{"shared":[{"id":"target"}]}""")]
    [InlineData("""{"shared":[{"id":"target","optional":true}],"server":[{"id":"target"}]}""")]
    [InlineData("""{"shared":[{"id":"target"}],"server":[{"id":"target","optional":true}]}""")]
    public void RequiredDeclarationFailsEvenForSourceAndTargetWithoutSidePayload(string json)
    {
        ModDependencyNode[] nodes = [Node("source", json, hasScript: false), Node("target", hasScript: false)];

        AssertUnavailable(nodes, "required");
        AssertUnavailable([nodes[0]], "required");
    }

    [Fact]
    public void OptionalTargetWithoutSidePayloadDoesNotCreateAnEdge()
    {
        ModDependencyNode[] nodes =
        [Node("source", """{"shared":[{"id":"target","optional":true}]}"""), Node("target", hasScript: false)];

        var result = ModDependencyResolver.Resolve(nodes, AssetTarget.Server);

        Assert.Equal<string>(["source", "target"], result.OrderedIds);
        Assert.Empty(result.DirectTargetsById["source"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedCookedOnlyTargetFailsEvenWhenOptionalButScriptCanSatisfyIt(bool optional)
    {
        string optionalToken = optional ? "true" : "false";
        var source = Node("source", $$"""{"shared":[{"id":"target","optional":{{optionalToken}}}]}""");
        var target = Node("target", hasScript: false, cooked: ModCookedAvailability.Rejected);

        AssertUnavailable([source, target], "rejected");

        var script = ModDependencyResolver.Resolve([source, target with { HasScriptPayload = true }], AssetTarget.Server);
        Assert.Equal<string>(["target", "source"], script.OrderedIds);
        Assert.Empty(script.ParticipatingCookedOnlyProviderIds);
    }

    [Fact]
    public void ReportsOnlyCookedOnlyCandidatesThatContributeToAnEdge()
    {
        ModDependencyNode[] nodes =
        [
            Node("a-source", """{"server":[{"id":"b-target"},{"id":"c-hybrid"}]}""",
                hasScript: false, cooked: ModCookedAvailability.AdmittedCandidate),
            Node("b-target", hasScript: false, cooked: ModCookedAvailability.AdmittedCandidate),
            Node("c-hybrid", cooked: ModCookedAvailability.AdmittedCandidate),
            Node("d-unused", """{"shared":[{"id":"missing","optional":true}]}""",
                hasScript: false, cooked: ModCookedAvailability.AdmittedCandidate),
            Node("e-capture-rejected", hasScript: false, cooked: ModCookedAvailability.Rejected),
            Node("f-script", """{"server":[{"id":"c-hybrid"}]}""", cooked: ModCookedAvailability.AdmittedCandidate),
            Node("g-inactive", """{"client":[{"id":"c-hybrid"}]}""",
                hasScript: false, cooked: ModCookedAvailability.AdmittedCandidate)
        ];

        var result = ModDependencyResolver.Resolve(nodes, AssetTarget.Server);

        Assert.True(result.ParticipatingCookedOnlyProviderIds.SetEquals(["a-source", "b-target"]));
        Assert.Equal<string>(["b-target", "c-hybrid", "a-source", "d-unused", "e-capture-rejected", "f-script", "g-inactive"], result.OrderedIds);
    }

    [Fact]
    public void SeededDagsUseOrdinalMinimumAmongReadyNodesRegardlessOfInputOrder()
    {
        var random = new Random(318805);
        string[] ids = Enumerable.Range(0, 20).Select(i => $"mod-{i:D2}").ToArray();
        for (int sample = 0; sample < 25; sample++)
        {
            string[] topologicalSequence = ids.OrderBy(_ => random.Next()).ToArray();
            var prerequisites = new Dictionary<string, string[]>(StringComparer.Ordinal);
            ModDependencyNode[] nodes = topologicalSequence.Select((id, i) =>
            {
                var targets = topologicalSequence.Take(i).Where(_ => random.Next(4) == 0).ToArray();
                prerequisites.Add(id, targets);
                return Node(id) with { Dependencies = ManifestForTargets(targets) };
            }).ToArray();

            var expected = ModDependencyResolver.Resolve(nodes, AssetTarget.Server);
            Assert.Equal(ids.Length, expected.OrderedIds.Length);
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in expected.OrderedIds)
            {
                string firstReady = ids.First(candidate => !emitted.Contains(candidate) && prerequisites[candidate].All(emitted.Contains));
                Assert.Equal(firstReady, id);
                Assert.True(expected.DirectTargetsById[id].SetEquals(prerequisites[id]));
                Assert.True(emitted.Add(id));
            }

            for (int permutation = 0; permutation < 4; permutation++)
            {
                random.Shuffle(nodes);
                var reordered = ModDependencyResolver.Resolve(nodes, AssetTarget.Server);
                Assert.Equal<string>(expected.OrderedIds, reordered.OrderedIds);
            }
        }
    }

    [Theory]
    [InlineData(AssetTarget.Client, "1.10.0", null)]
    [InlineData(AssetTarget.Client, "1.10.1", null)]
    [InlineData(AssetTarget.Client, "1.9.99", "shared")]
    [InlineData(AssetTarget.Client, "1.11.0", "client")]
    [InlineData(AssetTarget.Client, "2.0.0", "client")]
    [InlineData(AssetTarget.Server, "1.11.0", null)]
    [InlineData(AssetTarget.Server, "1.9.0", "shared")]
    public void EnforcesBothActiveNumericBoundsEvenForAvailableOptionalTarget(AssetTarget side, string version,
        string? failingScope)
    {
        ModDependencyNode[] nodes =
        [
            Node("source", """
                {
                  "shared":[{"id":"target","optional":true,"minVersion":"1.10.0"}],
                  "client":[{"id":"target","optional":true,"maxVersionExclusive":"1.11.0"}]
                }
                """),
            Node("target", version: version)
        ];

        if (failingScope is null)
        {
            var result = ModDependencyResolver.Resolve(nodes, side);
            Assert.Equal<string>(["target", "source"], result.OrderedIds);
            Assert.Equal("target", Assert.Single(result.DirectTargetsById["source"]));
            return;
        }

        var error = Assert.Throws<ModDependencyException>(() => ModDependencyResolver.Resolve(nodes, side));
        Assert.Contains($"Mod 'source' dependency 'target' on {side} ({failingScope})", error.Message);
        Assert.Contains(version, error.Message);
        Assert.Contains("incompatible", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(failingScope == "shared" ? "minVersion=1.10.0" : "maxVersionExclusive=1.11.0", error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("experimental build")]
    [InlineData("1.0")]
    [InlineData("1.0.0-rc1")]
    public void ConstrainedLegacyProviderIsIncompatibleInsteadOfAbsent(string? version)
    {
        ModDependencyNode[] nodes =
        [
            Node("source", """{"server":[{"id":"target","optional":true,"minVersion":"0.0.0","maxVersionExclusive":"2.0.0"}]}"""),
            Node("target", version: version, hasScript: false, cooked: ModCookedAvailability.AdmittedCandidate)
        ];

        var error = Assert.Throws<ModDependencyException>(() => ModDependencyResolver.Resolve(nodes, AssetTarget.Server));

        Assert.Contains("Mod 'source' dependency 'target' on Server (server)", error.Message);
        Assert.Contains("minVersion=0.0.0", error.Message);
        Assert.Contains("maxVersionExclusive=2.0.0", error.Message);
        Assert.Contains("incompatible", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MAJOR.MINOR.PATCH", error.Message);
        if (version is not null) Assert.Contains(version, error.Message);
    }

    [Fact]
    public void PresentOptionalCycleRejectsOnlyItsActiveSideAndNamesActualCycle()
    {
        ModDependencyNode[] nodes =
        [
            Node("a-downstream", """{"shared":[{"id":"b-cycle"}]}"""),
            Node("b-cycle", """{"shared":[{"id":"c-cycle","optional":true}]}"""),
            Node("c-cycle", """{"client":[{"id":"b-cycle","optional":true}]}"""),
            Node("other-ready")
        ];

        var server = ModDependencyResolver.Resolve(nodes, AssetTarget.Server);
        Assert.Equal<string>(["c-cycle", "b-cycle", "a-downstream", "other-ready"], server.OrderedIds);

        var error = Assert.Throws<ModDependencyException>(() => ModDependencyResolver.Resolve(nodes, AssetTarget.Client));
        Assert.Contains("Client", error.Message);
        Assert.Contains("cycle", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("b-cycle -> c-cycle -> b-cycle", error.Message);
        Assert.DoesNotContain("a-downstream", error.Message);
        Assert.DoesNotContain("other-ready", error.Message);
    }

    [Fact]
    public void SeededDagBackEdgesAreReportedWithoutDownstreamNoise()
    {
        var random = new Random(489141);
        for (int sample = 0; sample < 12; sample++)
        {
            string[] ids = Enumerable.Range(0, 20).Select(i => $"mod-{i:D2}").ToArray();
            random.Shuffle(ids);
            ModDependencyNode[] nodes = ids.Select((id, i) => i == 0 ? Node(id) :
                Node(id) with { Dependencies = ManifestForTargets([ids[i - 1]]) }).ToArray();
            Assert.Equal<string>(ids, ModDependencyResolver.Resolve(nodes, AssetTarget.Server).OrderedIds);

            int cycleEnd = random.Next(1, ids.Length - 1);
            nodes[0] = nodes[0] with { Dependencies = ManifestForTargets([ids[cycleEnd]]) };
            random.Shuffle(nodes);
            var error = Assert.Throws<ModDependencyException>(() => ModDependencyResolver.Resolve(nodes, AssetTarget.Server));
            foreach (string member in ids.Take(cycleEnd + 1)) Assert.Contains(member, error.Message);
            foreach (string downstream in ids.Skip(cycleEnd + 1)) Assert.DoesNotContain(downstream, error.Message);
        }
    }

    [Fact]
    public void HonorsPreCanceledResolution()
    {
        Assert.Throws<OperationCanceledException>(() =>
            ModDependencyResolver.Resolve([Node("source")], AssetTarget.Server, new CancellationToken(canceled: true)));
    }

    private static void AssertUnavailable(ModDependencyNode[] nodes, string reason)
    {
        var error = Assert.Throws<ModDependencyException>(() => ModDependencyResolver.Resolve(nodes, AssetTarget.Server));
        Assert.Contains("'source'", error.Message);
        Assert.Contains("'target'", error.Message);
        Assert.Contains("Server", error.Message);
        Assert.Contains(reason, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ModDependencyNode Node(string id, string? json = null, string? version = null, bool hasScript = true,
        ModCookedAvailability cooked = ModCookedAvailability.Absent) =>
        new(id, version, ModDependencyManifest.Parse(id, json is null ? null : JToken.Parse(json)), hasScript, cooked);

    private static ModDependencyManifest ManifestForTargets(IEnumerable<string> targets) =>
        new([.. targets.Select(id => new ModDependencyDeclaration(id, false, null, null))], [], []);
}
