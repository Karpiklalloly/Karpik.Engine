using System.Runtime.CompilerServices;
using GTweens.Contexts;
using GTweens.Tweens;

namespace Karpik.Engine.Shared;

public class Tween
{
    private static readonly GTweensContext _context = new();
    private static readonly GTweensContext _pausableContext = new();

    private static ThreadLocal<Tween> _instance = new ThreadLocal<Tween>(() => new Tween());
    public static Tween Instance => _instance.Value;

    private Tween() { }

    public static void Add(GTween tween, bool pausable)
    {
        if (pausable)
        {
            _pausableContext.Play(tween);
        }
        else
        {
            _context.Play(tween);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Update(double deltaTime)
    {
        _context.Tick((float)deltaTime);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdatePausable(double deltaTime)
    {
        _pausableContext.Tick((float)deltaTime);
    }
}