namespace Karpik.Engine.Client.Graphics.Core.VeldridLike;

public readonly struct PipelineDescription
{
    // 1. Шейдеры (Обязательно)
    public readonly IShader VertexShader;
    public readonly IShader FragmentShader;

    // 2. Входные данные (Как читать вершины из памяти)
    public readonly VertexLayoutDescription[] VertexLayouts;
    
    // 3. Формат ресурсов (Какие Uniform-буферы и текстуры ожидает этот шейдер)
    public readonly IResourceLayout[] ResourceLayouts;

    // 4. Аппаратные состояния (Как рисовать)
    public readonly PrimitiveTopology Topology;
    public readonly RasterizerStateDescription RasterizerState;
    public readonly DepthStencilStateDescription DepthStencilState;
    public readonly BlendStateDescription BlendState;

    // 5. Формат вывода (В какие текстуры мы рисуем, нужно для валидации в Vulkan/DX12)
    public readonly PixelFormat[] OutputColorFormats;
    public readonly PixelFormat? OutputDepthFormat;

    // Огромный конструктор, собирающий всё воедино (обычно заполняется через Builder-паттерн в пользовательском коде)
    public PipelineDescription(
        IShader vertexShader,
        IShader fragmentShader,
        VertexLayoutDescription[] vertexLayouts,
        IResourceLayout[] resourceLayouts,
        PrimitiveTopology topology,
        RasterizerStateDescription rasterizerState,
        DepthStencilStateDescription depthStencilState,
        BlendStateDescription blendState,
        PixelFormat[] outputColorFormats,
        PixelFormat? outputDepthFormat)
    {
        VertexShader = vertexShader;
        FragmentShader = fragmentShader;
        VertexLayouts = vertexLayouts;
        ResourceLayouts = resourceLayouts;
        Topology = topology;
        RasterizerState = rasterizerState;
        DepthStencilState = depthStencilState;
        BlendState = blendState;
        OutputColorFormats = outputColorFormats;
        OutputDepthFormat = outputDepthFormat;
    }
}