using System.Numerics;
using System.Text;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;

namespace TestVeldrid;

class Program
{
    private static GraphicsDevice _graphicsDevice;

    private static CommandList _commandList;
    private static DeviceBuffer _vertexBuffer;
    private static DeviceBuffer _indexBuffer;
    private static Shader[] _shaders;
    private static Pipeline _pipeline;

    private const string VertexCode = """

                                      #version 450

                                      layout(location = 0) in vec2 Position;
                                      layout(location = 1) in vec4 Color;

                                      layout(location = 0) out vec4 fsin_Color;

                                      void main()
                                      {
                                          gl_Position = vec4(Position, 0, 1);
                                          fsin_Color = Color;
                                      }
                                      """;

    private const string FragmentCode = """

                                        #version 450

                                        layout(location = 0) in vec4 fsin_Color;
                                        layout(location = 0) out vec4 fsout_Color;

                                        void main()
                                        {
                                            fsout_Color = fsin_Color;
                                        }
                                        """;

    static void Main(string[] args)
    {
        WindowCreateInfo windowCI = new WindowCreateInfo()
        {
            X = 100,
            Y = 100,
            WindowWidth = 960,
            WindowHeight = 540,
            WindowTitle = "Veldrid Tutorial"
        };
        Sdl2Window window = VeldridStartup.CreateWindow(ref windowCI);

        _graphicsDevice = VeldridStartup.CreateGraphicsDevice(window);

        CreateResources();

        while (window.Exists)
        {
            window.PumpEvents();
            Draw();
        }
        DisposeResources();
    }

    private static void CreateResources()
    {
        ResourceFactory factory = _graphicsDevice.ResourceFactory;

        VertexPositionColor[] quadVertices =
        {
            new(new Vector2(-0.75f, 0.75f), RgbaFloat.Red),
            new(new Vector2(0.75f, 0.75f), RgbaFloat.Green),
            new(new Vector2(-0.75f, -0.75f), RgbaFloat.Blue),
            new(new Vector2(0.75f, -0.75f), RgbaFloat.Yellow)
        };

        ushort[] quadIndices = { 0, 1, 2, 3 };

        _vertexBuffer =
            factory.CreateBuffer(new BufferDescription(4 * VertexPositionColor.SizeInBytes, BufferUsage.VertexBuffer));
        _indexBuffer = factory.CreateBuffer(new BufferDescription(4 * sizeof(ushort), BufferUsage.IndexBuffer));

        _graphicsDevice.UpdateBuffer(_vertexBuffer, 0, quadVertices);
        _graphicsDevice.UpdateBuffer(_indexBuffer, 0, quadIndices);

        VertexLayoutDescription vertexLayout = new(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate,
                VertexElementFormat.Float2),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));

        ShaderDescription vertexShaderDesc = new(
            ShaderStages.Vertex,
            Encoding.UTF8.GetBytes(VertexCode),
            "main");
        ShaderDescription fragmentShaderDesc = new(
            ShaderStages.Fragment,
            Encoding.UTF8.GetBytes(FragmentCode),
            "main");

        _shaders = factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);

        GraphicsPipelineDescription pipelineDescription = new()
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = new DepthStencilStateDescription(
                depthTestEnabled: true,
                depthWriteEnabled: true,
                comparisonKind: ComparisonKind.LessEqual),
            RasterizerState = new RasterizerStateDescription(
                cullMode: FaceCullMode.Back,
                fillMode: PolygonFillMode.Solid,
                frontFace: FrontFace.Clockwise,
                depthClipEnabled: true,
                scissorTestEnabled: false),
            PrimitiveTopology = PrimitiveTopology.TriangleStrip,
            ResourceLayouts = [],
            ShaderSet = new ShaderSetDescription(
                vertexLayouts: [vertexLayout],
                shaders: _shaders),
            Outputs = _graphicsDevice.SwapchainFramebuffer.OutputDescription
        };

        _pipeline = factory.CreateGraphicsPipeline(pipelineDescription);

        _commandList = factory.CreateCommandList();
    }

    private static void Draw()
    {
        _commandList.Begin();
        _commandList.SetFramebuffer(_graphicsDevice.SwapchainFramebuffer);
        _commandList.ClearColorTarget(0, RgbaFloat.Black);
        _commandList.SetVertexBuffer(0, _vertexBuffer);
        _commandList.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
        _commandList.SetPipeline(_pipeline);
        _commandList.DrawIndexed(
            indexCount: 4,
            instanceCount: 1,
            indexStart: 0,
            vertexOffset: 0,
            instanceStart: 0);
        _commandList.End();
        _graphicsDevice.SubmitCommands(_commandList);
        _graphicsDevice.SwapBuffers();
    }
    
    private static void DisposeResources()
    {
        _pipeline.Dispose();
        foreach (var shader in _shaders)
        {
            shader.Dispose();
        }
        _commandList.Dispose();
        _vertexBuffer.Dispose();
        _indexBuffer.Dispose();
        _graphicsDevice.Dispose();
    }
}
