namespace Karpik.Engine.Client.Graphics.Core.VeldridLike;

public enum CullMode : byte
{
    None,   // Рисовать всё (двусторонние материалы, листья, трава)
    Front,  // Отсекать передние грани (используется редко)
    Back    // Отсекать задние грани (Стандарт для 99% 3D моделей)
}