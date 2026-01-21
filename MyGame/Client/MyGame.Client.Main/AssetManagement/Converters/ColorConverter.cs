using System.Drawing;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Karpik.Engine.MyGame.Client.Main;

public class ColorConverter : CustomCreationConverter<Color>
{
    public override bool CanWrite => true;
    public override bool CanRead => true;
    public ColorConverter(){ }
    public override Color Create(Type objectType)
    {
        return new Color();
    }
    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        JObject jObject = JObject.Load(reader);
        Color target = Create(objectType);
        target = Color.FromArgb(jObject["A"]!.Value<Int32>(), jObject["R"]!.Value<Int32>(), jObject["G"]!.Value<Int32>(), jObject["B"]!.Value<Int32>());
        return target;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        var color = (Color)value!;
        
        writer.WriteStartObject();
        Write("R", color.R, writer);
        Write("G", color.G, writer);
        Write("B", color.B, writer);
        Write("A", color.A, writer);
        writer.WriteEndObject();
    }

    private void Write(string channel, int value, JsonWriter writer)
    {
        writer.WritePropertyName(channel);
        writer.WriteValue(value);
    }
}