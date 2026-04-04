namespace Karpik.Engine.Client.Graphics.Core.VeldridLike;

public readonly struct DepthStencilStateDescription
{
    public readonly bool DepthTestEnabled;
    public readonly bool DepthWriteEnabled;
    public readonly CompareFunction DepthComparison;

    public DepthStencilStateDescription(bool testEnabled, bool writeEnabled, CompareFunction comparison)
    {
        DepthTestEnabled = testEnabled; 
        DepthWriteEnabled = writeEnabled; 
        DepthComparison = comparison;
    }

    // Стандартное 3D (Z-Buffer включен)
    public static readonly DepthStencilStateDescription DepthReadWrite = 
        new(true, true, CompareFunction.LessEqual);
    
    // Стандартное 2D / UI (Z-Buffer выключен)
    public static readonly DepthStencilStateDescription Disabled = 
        new(false, false, CompareFunction.Always);
}