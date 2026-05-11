namespace Karpik.Engine.Core;

public interface ISystemLate : ISystem
{
    public void LateRun();
}