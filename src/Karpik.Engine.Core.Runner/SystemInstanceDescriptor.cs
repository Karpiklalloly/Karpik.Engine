namespace Karpik.Engine.Core.Runner;

public readonly record struct SystemInstanceDescriptor(
    string id,
    ISystem System,
    string Layer,
    int Order);