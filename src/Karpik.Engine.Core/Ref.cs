namespace Karpik.Engine.Core;

public class Ref<T> where T : struct
{
    public T Value;
    
    public Ref(T value = default)
    {
        Value = value;
    }
}