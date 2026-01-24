using System.CodeDom.Compiler;
using Karpik.Engine.Core.Hot;
using Karpik.Engine.Shared.AssetManagement.Core;
using Newtonsoft.Json;

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
                    snapshot.Components = objs.Cast<IEcsComponentMember>().ToArray();
                    entitySnapshots.Add(snapshot);
                }

                return JsonConvert.SerializeObject(entitySnapshots, new JsonSerializerSettings()
                {
                    Formatting = Formatting.Indented,
                    TypeNameHandling = TypeNameHandling.Objects,
                    TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                    Converters = [new ComponentArrayConverter()]
                });
            }
        }

        public static T FromSnapshot<T>(string snapshots, TypeMapper map) where T : EcsWorld, new()
        {
            // TODO: добавить конвертер массива компонентов
            var list = JsonConvert.DeserializeObject<List<EntitySnapshot>>(snapshots, new JsonSerializerSettings()
            {
                Formatting = Formatting.Indented,
                TypeNameHandling = TypeNameHandling.Objects,
                TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                SerializationBinder = new LooseAssemblyNameBinder(),
                Converters = [new ComponentArrayConverter()]
            });

            T newWorld = new();

            foreach (var entitySnapshot in list)
            {
                newWorld.NewEntity(entitySnapshot.Id);
                foreach (var component in entitySnapshot.Components)
                {
                    var newType = map.GetNewType(component.GetType());
                    if (!newWorld.TryFindPoolInstance(newType, out var pool))
                    {
                        var method = typeof(EcsPoolExtensions).GetMethod(nameof(EcsPoolExtensions.GetPool));
                        if (method is null) throw new NullInstanceException();
                        var genericMethod = method.MakeGenericMethod(newType);

                        pool = (IEcsPool)genericMethod.Invoke(null, new []{(object)newWorld});
                        if (pool is null) throw new NullInstanceException();
                    }
                    pool.AddRaw(entitySnapshot.Id, component);
                }
                
            }

            return newWorld;
        }
    }
}