namespace Karpik.Engine.Shared.ECS;

[Serializable]
public record EntitySnapshot
{
    public int Id = -1;
    public List<IEcsComponentMember> Components = [];
}