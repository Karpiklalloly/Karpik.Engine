namespace Karpik.Engine.Shared.Modding;

public struct Optional<TValue, TError>(TValue? value, TError? error = default)
{
    public TValue? Value = value;
    public TError? Error = error;

    public static implicit operator Optional<TValue, TError>(TValue x)
    {
        return new Optional<TValue, TError>()
        {
            Value = x
        };
    }
    
    public static implicit operator Optional<TValue, TError>((TValue? x, TError message) pair)
    {
        return new Optional<TValue, TError>()
        {
            Value = pair.x,
            Error = pair.message
        };
    }
}