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
        Console.WriteLine($"[TWEEN_SYSTEM] Adding tween (pausable: {pausable})");
        if (pausable)
        {
            _pausableContext.Play(tween);
            Console.WriteLine($"[TWEEN_SYSTEM] Added to pausable context");
        }
        else
        {
            _context.Play(tween);
            Console.WriteLine($"[TWEEN_SYSTEM] Added to main context");
        }
    }

    public void Update(double deltaTime)
    {
        Console.WriteLine($"[TWEEN_SYSTEM] Ticking main context with deltaTime: {deltaTime}");
        _context.Tick((float)deltaTime);
    }

    public void UpdatePausable(double deltaTime)
    {
        Console.WriteLine($"[TWEEN_SYSTEM] Ticking pausable context with deltaTime: {deltaTime}");
        _pausableContext.Tick((float)deltaTime);
    }
}