namespace Karpik.Engine.Shared.Modding.Bindings;

public sealed record ComponentFieldDescriptor(string Name, bool CanWrite, IComponentField<float> F32);
