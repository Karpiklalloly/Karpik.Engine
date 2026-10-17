namespace Karpik.Engine.Core.Runner;

public readonly record struct SystemDescriptor(
    Type SystemType,
    string Layer,
    int Order);