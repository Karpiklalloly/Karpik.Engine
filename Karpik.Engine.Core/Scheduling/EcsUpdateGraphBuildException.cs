namespace Karpik.Engine.Shared.ECS.Scheduling;

public sealed class EcsUpdateGraphBuildException : Exception
{
    public EcsUpdateGraphBuildException(string message) : base(message)
    {
    }
}
