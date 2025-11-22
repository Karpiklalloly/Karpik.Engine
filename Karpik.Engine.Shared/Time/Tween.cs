using System.Runtime.CompilerServices;
using GTweens.Contexts;
using GTweens.Tweens;

namespace Karpik.Engine.Shared;

public class Tween
{
    private static readonly GTweensContext _context = new();
    private static readonly GTweensContext _pausableContext = new();

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

    public void Update(double deltaTime)
    {
        _context.Tick((float)deltaTime);
    }

    public void UpdatePausable(double deltaTime)
    {
        _pausableContext.Tick((float)deltaTime);
    }
}