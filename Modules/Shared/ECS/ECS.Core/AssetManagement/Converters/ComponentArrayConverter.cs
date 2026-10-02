using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Karpik.Engine.Shared.ECS;

public class ComponentArrayConverter(ILogger<ComponentArrayConverter> logger) : JsonConverter<IEcsComponentMember[]>
{
    private const string TypePropertyName = "$type";

    // Deserialization must stay on the non-generic ToObject(Type, JsonSerializer)
    // overload: runtime generic-method instantiation has no native code under AOT,
    // and published static hosts restore hot-reload state through this converter.
    private readonly ConcurrentDictionary<Type, byte> _verifiedComponentTypes = new();

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
                        _verifiedComponentTypes.TryAdd(componentType, 0);
                        deserializedComponent = obj.ToObject(componentType, serializer) as IEcsComponentMember;
                    }
                    else
                    {
                        logger.LogError("Error: Could not find or assignable type for name '{TypeName}'.", typeName);
                    }
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
}
