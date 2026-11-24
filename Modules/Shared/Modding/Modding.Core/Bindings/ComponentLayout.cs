using System.Text;

namespace Karpik.Engine.Shared.Modding.Bindings;

internal readonly record struct ComponentLeaf(string Name, SchemaScalarKind Kind, ComponentPathSegment[] Path,
    string ColumnKey, int Offset, int Width, string? PointerTypeId);

internal static class ComponentLayout
{
    internal static ComponentLeaf[] Expand(ComponentSchema schema)
    {
        var leaves = new List<ComponentLeaf>();
        var pending = new Stack<(SchemaValue Value, ComponentPathSegment[] Path, int Offset, int Width)>();
        pending.Push((SchemaValue.Struct(schema.Fields), [], 0, 1));
        while (pending.TryPop(out var frame))
        {
            SchemaValue value = frame.Value;
            if (value.NodeKind == SchemaValueKind.Struct)
            {
                for (int i = value.Fields.Length - 1; i >= 0; i--)
                {
                    SchemaField field = value.Fields[i];
                    pending.Push((field.Value ?? SchemaValue.Scalar(field.Kind), Append(frame.Path, new(field.Name)), frame.Offset, frame.Width));
                }
            }
            else if (value.NodeKind == SchemaValueKind.Fixed)
            {
                for (int i = value.Length; i >= 1; i--)
                    pending.Push((value.Element!, Append(frame.Path, new(null, i)), checked(frame.Offset * value.Length + i - 1), checked(frame.Width * value.Length)));
            }
            else
            {
                var display = new StringBuilder(); var key = new StringBuilder();
                foreach (var segment in frame.Path)
                {
                    if (segment.Name is { } name)
                    {
                        if (display.Length != 0) display.Append('.');
                        display.Append(name); key.Append(name.Length).Append(':').Append(name).Append('/');
                    }
                    else { display.Append('[').Append(segment.Index).Append(']'); key.Append("#/ "); }
                }
                leaves.Add(new(display.ToString(), value.NodeKind == SchemaValueKind.Pointer ? SchemaScalarKind.Pointer : value.ScalarKind,
                    frame.Path, key.ToString(), frame.Offset, frame.Width, value.PointerTypeId));
            }
        }
        return leaves.ToArray();
    }

    private static ComponentPathSegment[] Append(ComponentPathSegment[] parent, ComponentPathSegment segment)
    {
        var result = new ComponentPathSegment[parent.Length + 1];
        parent.CopyTo(result, 0); result[^1] = segment; return result;
    }
}
