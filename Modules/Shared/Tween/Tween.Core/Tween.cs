using System.Runtime.CompilerServices;
using GTweens.Contexts;
using GTweens.Tweens;

namespace Karpik.Engine.Shared.Tweening;

public class Tween
{
    private readonly GTweensContext _context = new();
    private readonly GTweensContext _pausableContext = new();

    public void Add(GTween tween, bool pausable)
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