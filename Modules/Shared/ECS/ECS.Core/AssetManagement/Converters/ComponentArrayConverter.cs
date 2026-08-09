using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Karpik.Engine.Shared.ECS;

public class ComponentArrayConverter(ILogger<ComponentArrayConverter> logger) : JsonConverter<IEcsComponentMember[]>
{
    private const string TypePropertyName = "$type";

    private MethodInfo? _genericToObjectMethodInfo;
    private readonly Lock _methodInfoLock = new();
    private readonly ConcurrentDictionary<Type, MethodInfo> _closedToObjectMethodCache = new();
    
    public override void WriteJson(JsonWriter writer, IEcsComponentMember[]? value, JsonSerializer serializer)
    {
        writer.WriteStartArray();
        if (value is not null)
        {
            foreach (var component in value)
            {
                var obj = JObject.FromObject(component, serializer);
                obj.WriteTo(writer);
            }
        }
        writer.WriteEndArray();
    }

    public override IEcsComponentMember[]? ReadJson(JsonReader reader, Type objectType, IEcsComponentMember[]? existingValue, bool hasExistingValue, JsonSerializer serializer)
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

            JToken? typeToken = obj[TypePropertyName];
            if (typeToken?.Type is not JTokenType.String)
            {
                logger.LogError("Missing or invalid '{TypePropertyName}' property. Skipping object: {obj}", TypePropertyName, obj.ToString(Formatting.None));
                continue;
            }
            
            IEcsComponentMember? deserializedComponent = null;
            
            string? typeName = typeToken.Value<string>();
            if (typeName is not null)
            {
                string[] type = typeName.Split(',');
                obj.Remove(TypePropertyName);
            
                try
                {
                    Type componentType;
                    if (type.Length > 1)
                    {
                        componentType = serializer.SerializationBinder.BindToType(type[1].Trim(), type[0].Trim());
                    }
                    else
                    {
                        componentType = serializer.SerializationBinder.BindToType(null, typeName);
                    }
                

                    if (typeof(IEcsComponentMember).IsAssignableFrom(componentType))
                    {
                        if (!_closedToObjectMethodCache.TryGetValue(componentType, out var closedMethod))
                        {
                            closedMethod = _genericToObjectMethodInfo.MakeGenericMethod(componentType);
                            _closedToObjectMethodCache.TryAdd(componentType, closedMethod);
                        }

                        deserializedComponent = (IEcsComponentMember?)closedMethod.Invoke(obj, [serializer]);
                    }
                    else
                    {
                        logger.LogError("Error: Could not find or assignable type for name '{TypeName}'.", typeName);
                    }
                }
                catch (TargetInvocationException tie)
                {
                    logger.LogError("Error invoking ToObject<{TypeName}>: {InnerExceptionMessage}\nJSON: {obj}", typeName, tie.InnerException?.Message ?? tie.Message, obj.ToString(Formatting.None));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error deserializing component type '{TypeName}' using reflection: {Exception}\nJSON: {obj}", typeName, ex.Message, obj.ToString(Formatting.None));
                }
            }

            if (deserializedComponent != null)
            {
                components.Add(deserializedComponent);
            }
        }

        return components.ToArray();
    }
    
    private void InitializeGenericToObjectMethodInfo()
    {
        if (_genericToObjectMethodInfo != null) return;

        lock (_methodInfoLock)
        {
            if (_genericToObjectMethodInfo != null) return;

            MethodInfo? foundMethod = typeof(JObject).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m =>
                    m.Name == "ToObject" &&
                    m.IsGenericMethodDefinition &&
                    m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == typeof(JsonSerializer)
                );

            if (foundMethod == null)
            {
                logger.LogCritical("Could not find method info for JObject.ToObject<T>(JsonSerializer).");
            }

            _genericToObjectMethodInfo = foundMethod;
        }
    }
}