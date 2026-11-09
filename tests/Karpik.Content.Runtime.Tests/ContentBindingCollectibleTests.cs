using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Xunit;

namespace Karpik.Content.Runtime.Tests;

public sealed class ContentBindingCollectibleTests
{
    [Fact]
    public void Disposed_json_owner_does_not_retain_collectible_payload_metadata()
    {
        AssertCollected(CreateLoadAndDispose(true));
    }

    [Fact]
    public void Json_metadata_without_bindings_is_collectible_negative_control()
    {
        AssertCollected(CreateLoadAndDispose(false));
    }

    private static void AssertCollected(WeakReference context)
    {
        for (int i = 0; i < 20 && context.IsAlive; i++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Thread.Sleep(10);
        }
        Assert.False(context.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateLoadAndDispose(bool useBindings)
    {
        // Use real source-generated metadata, matching the public API's AOT-safe
        // contract. Reflection here only crosses the test ALC boundary.
        using var image = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "BindingFixture", "Karpik.Content.BindingFixture.dll"));
        var load = new AssemblyLoadContext("binding-metadata", isCollectible: true);
        var weak = new WeakReference(load);
        Assembly assembly = load.LoadFromStream(image);
        Type payload = assembly.GetType("CollectibleUi")!;
        object metadata = assembly.GetType("MetadataFactory")!.GetMethod("Create")!.Invoke(null, null)!;
        if (!useBindings)
        {
            _ = System.Text.Json.JsonSerializer.Deserialize("{\"Title\":\"collectible\"}",
                (System.Text.Json.Serialization.Metadata.JsonTypeInfo)metadata);
            load.Unload();
            return weak;
        }

        var id = new AssetId(Guid.NewGuid());
        var store = new Store();
        var registry = new ContentRegistry(null!, store);
        registry.RegisterManifest(new ContentManifest(1,
            [new(id, "raw-json", "ui", "settings", "source", "ui", store.Bytes.Length, [])]), store);
        var bindings = new ContentBindings(registry, registry, new EmptyChanges());
        try
        {
            MethodInfo bind = typeof(ContentBindings).GetMethods().Single(method =>
                method.Name == "BindJson" && method.GetParameters().Length == 2).MakeGenericMethod(payload);
            object handle = bind.Invoke(bindings, [id, metadata])!;
            MethodInfo status = typeof(ContentBindings).GetMethod("TryGetStatus")!.MakeGenericMethod(payload);
            MethodInfo error = typeof(ContentBindings).GetMethod("GetError")!.MakeGenericMethod(payload);
            object?[] arguments = [handle, null];
            var deadline = Stopwatch.StartNew();
            while (true)
            {
                bindings.AdvanceBoundary();
                Assert.True((bool)status.Invoke(bindings, arguments)!);
                var value = (ContentBindingStatus)arguments[1]!;
                if (value.State == ContentBindingState.Ready) break;
                Assert.True(value.State != ContentBindingState.Failed,
                    error.Invoke(bindings, [handle])?.ToString());
                Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(10), "Collectible UI never loaded.");
                Thread.Sleep(1);
            }
        }
        finally
        {
            bindings.DisposeAsync().AsTask().GetAwaiter().GetResult();
            registry.DisposeAsync().AsTask().GetAwaiter().GetResult();
            load.Unload();
        }
        return weak;
    }

    private sealed class Store : IContentStore
    {
        public readonly byte[] Bytes = Encoding.UTF8.GetBytes("{\"Title\":\"collectible\"}");
        public ReadOnlyMemory<byte> Get(string locator) => Bytes;
        public Task<ReadOnlyMemory<byte>> GetAsync(string locator, CancellationToken ct = default) => Task.FromResult(Get(locator));
    }

    private sealed class EmptyChanges : IContentChangeSource
    {
        public ReadOnlySpan<ContentAssetChange> Changes => [];
        public ulong Sequence => 0;
    }
}
