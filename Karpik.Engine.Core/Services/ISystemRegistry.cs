namespace Karpik.Engine.Core;

public interface ISystemRegistry
{
    public void Add<T>(string layer = "BASIC_LAYER", int order = 0) where T : class, ISystem;
}