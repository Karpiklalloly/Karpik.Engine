namespace Karpik.Engine.Shared.Modding.Bindings;

public readonly record struct ComponentQueryEntry(string ComponentId, bool Readonly = true);
public sealed record ComponentQueryDefinition(string Name, ComponentQueryEntry[] Inc, ComponentQueryEntry[] Exc, ComponentQueryEntry[] Opt);
