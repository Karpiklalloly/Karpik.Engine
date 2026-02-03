using System.CodeDom.Compiler;
using System.Text;
using Karpik.Engine.Core.Hot;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Karpik.Engine.Shared.ECS;

public static class EcsWorldExtensions
{
    extension(EcsWorld world)
    {
        public string Snapshot
        {
            get
            {
                List<EntitySnapshot> entitySnapshots = [];
                var entities = world.Entities;
                foreach (var e in entities)
                {
                    EntitySnapshot snapshot = new EntitySnapshot();
                    snapshot.Id = e;
                    List<object> objs = [];
                    world.GetComponentsFor(e, objs);
                    snapshot.Components = new ComponentsTemplate(objs.Cast<IEcsComponentMember>().ToArray());
                    entitySnapshots.Add(snapshot);
                }
                
                return JsonConvert.SerializeObject(entitySnapshots, new JsonSerializerSettings()
                {
                    Formatting = Formatting.Indented,
                    TypeNameHandling = TypeNameHandling.Objects,
                    TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                    Converters = [new ComponentArrayConverter()],
                    ContractResolver = new DefaultContractResolver()
                });
            }
        }

        public static async JobHandle FromSnapshot(EcsWorld newWorld, string snapshots, IAssetsManager manager)
        {
            var list = JsonConvert.DeserializeObject<List<EntitySnapshot>>(snapshots, new JsonSerializerSettings()
            {
                Formatting = Formatting.Indented,
                TypeNameHandling = TypeNameHandling.Objects,
                TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                SerializationBinder = new LooseAssemblyNameBinder(),
                Converters = [new ComponentArrayConverter()],
                ContractResolver = new DefaultContractResolver()
            })!;

            var entities = newWorld.Entities;
            foreach (var e in entities)
            {
                newWorld.DelEntity(e);
            }
            
            await foreach (var entitySnapshot in list.ToAsyncEnumerable())
            {
                newWorld.NewEntity(entitySnapshot.Id);
                entitySnapshot.Components.OnLoad(manager);
                await entitySnapshot.Components.ApplyTo(entitySnapshot.Id, newWorld);
            }
        }
    }
}