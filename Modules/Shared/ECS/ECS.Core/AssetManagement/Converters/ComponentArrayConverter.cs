using System.Collections.Concurrent;
using System.Reflection;
using Karpik.Engine.Shared.Log;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Karpik.Engine.Shared.ECS;

public class ComponentArrayConverter : JsonConverter<IEcsComponentMember[]>
{
    private const string TypePropertyName = "$type";

    private static MethodInfo _genericToObjectMethodInfo;
    private static readonly Lock _methodInfoLock = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> ClosedToObjectMethodCache = new();
    
    public override void WriteJson(JsonWriter writer, IEcsComponentMember[] value, JsonSerializer serializer)
    {
        writer.WriteStartArray();
        foreach (var component in value)
        {
            var obj = JObject.FromObject(component, serializer);
            obj.WriteTo(writer);
        }
        writer.WriteEndArray();
    }

    public override IEcsComponentMember[] ReadJson(JsonReader reader, Type objectType, IEcsComponentMember[] existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;

        JArray jArray = JArray.Load(reader);
        var components = new List<IEcsComponentMember>(jArray.Count);

        InitializeGenericToObjectMethodInfo();
        if (_genericToObjectMethodInfo == null)
        {
             throw new InvalidOperationException("Could not find the generic method JObject.ToObject<T>(JsonSerializer).");
        }
        
        foreach (JToken token in jArray)
        {
            if (token.Type != JTokenType.Object) continue;
            JObject obj = (JObject)token;

            JToken typeToken = obj[TypePropertyName];
            if (typeToken is not { Type: JTokenType.String })
            {
                Logger.Instance.Log(nameof(ComponentArrayConverter), $"Error: Missing or invalid '{TypePropertyName}' property. Skipping object: {obj.ToString(Formatting.None)}", LogLevel.Error);
                 continue;
            }
            string typeName = typeToken.Value<string>();
            var type = typeName.Split(',');
            obj.Remove(TypePropertyName);

            IEcsComponentMember deserializedComponent = null;
            try
            {
                Type? componentType;
                if (type.Length > 1)
                {
                    componentType = serializer.SerializationBinder.BindToType(type[1].Trim(), type[0].Trim());
                }
                else
                {
                    componentType = serializer.SerializationBinder.BindToType(null, typeName);;
                }
                

                if (componentType is not null && typeof(IEcsComponentMember).IsAssignableFrom(componentType))
                {
                    if (!ClosedToObjectMethodCache.TryGetValue(componentType, out var closedMethod))
                    {
                        closedMethod = _genericToObjectMethodInfo.MakeGenericMethod(componentType);
                        ClosedToObjectMethodCache.TryAdd(componentType, closedMethod);
                    }

                    deserializedComponent = (IEcsComponentMember)closedMethod.Invoke(obj, [serializer]);
                }
                else
                {
                     Console.WriteLine($"[ComponentArrayConverter] Error: Could not find or assignable type for name '{typeName}'.");
                }
            }
            catch (TargetInvocationException tie)
            {
                 Console.WriteLine($"[ComponentArrayConverter] Error invoking ToObject<{typeName}>: {tie.InnerException?.Message ?? tie.Message}\nJSON: {obj.ToString(Formatting.None)}");
            }
            catch (Exception ex)
            {
                 Console.WriteLine($"[ComponentArrayConverter] Error deserializing component type '{typeName}' using reflection: {ex.Message}\nJSON: {obj.ToString(Formatting.None)}");
            }

            if (deserializedComponent != null)
            {
                components.Add(deserializedComponent);
            }
        }

        return components.ToArray();
    }
    
    private static void InitializeGenericToObjectMethodInfo()
    {
        if (_genericToObjectMethodInfo != null) return;

        lock (_methodInfoLock)
        {
            if (_genericToObjectMethodInfo != null) return;

            MethodInfo foundMethod = typeof(JObject).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m =>
                    m.Name == "ToObject" &&
                    m.IsGenericMethodDefinition &&
                    m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == typeof(JsonSerializer)
                );

            if (foundMethod == null)
            {
                 Console.WriteLine("[ComponentArrayConverter] CRITICAL ERROR: Could not find method info for JObject.ToObject<T>(JsonSerializer).");
            }

            _genericToObjectMethodInfo = foundMethod;
        }
    }


    private static Type FindTypeByName(string typeName)
    {
        Type foundType = Type.GetType(typeName, throwOnError: false, ignoreCase: true);
        if (foundType != null) return foundType;
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            foundType = asm.GetType(typeName, throwOnError: false, ignoreCase: true);
            if (foundType != null) return foundType;
            
            var types = asm.GetTypes().Where(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (types.Count == 1) return types[0];
            if (types.Count > 1) {
                Console.WriteLine($"[FindTypeByName] Ambiguous type name '{typeName}' found in assembly {asm.FullName}. Provide a more qualified name.");
                return null;
            }
        }
        return null;
    }
}