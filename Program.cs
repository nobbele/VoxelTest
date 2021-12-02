using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Silk.NET.GLFW;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using VoxelTest.Graphics;
using WGPU.NET;
using Image = SixLabors.ImageSharp.Image;

namespace VoxelTest
{
    public readonly record struct Vector3 : IVertexFieldType
    {
        public readonly float X, Y, Z;

        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        Wgpu.VertexFormat IVertexFieldType.VertexFormat => Wgpu.VertexFormat.Float32x3;
    }
    public readonly record struct Vector2 : IVertexFieldType
    {
        public readonly float X, Y;

        public Vector2(float x, float y)
        {
            X = x;
            Y = y;
        }

        Wgpu.VertexFormat IVertexFieldType.VertexFormat => Wgpu.VertexFormat.Float32x2;
    }
    public readonly record struct Color : IVertexFieldType
    {
        public readonly float R, G, B, A;

        public Color(float r, float g, float b, float a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        Wgpu.VertexFormat IVertexFieldType.VertexFormat => Wgpu.VertexFormat.Float32x4;
    }

    public readonly record struct Vertex(Vector3 Position, Vector2 UV);

    class Program
    {
        static unsafe void Main()
        {
            Glfw glfw = GlfwProvider.GLFW.Value;
            if (!glfw.Init())
                throw new Exception("GLFW failed to initialize");

            glfw.WindowHint(WindowHintClientApi.ClientApi, ClientApi.NoApi);

            WindowHandle* window = glfw.CreateWindow(640, 480, "Wgpu.NET test", null, null);
            if (window is null)
                throw new Exception("Failed to open window");

            var nativeWindow = new GlfwNativeWindow(glfw, window);
            var gfx = new GraphicsContext(in nativeWindow);

            Console.WriteLine("Loading texture");
            Wgpu.TextureImpl diffuseTexture;
            using (var image = Image.Load<Rgba32>("happy-tree.png"))
            {
                diffuseTexture = WgpuFactory.CreateTextureWithData(gfx, image);
            }

            var (diffuseBindGroup, textureBindGroupLayout) = WgpuFactory.CreateBindGroup(gfx, diffuseTexture);
            Console.WriteLine("Loaded texture\n");

            var (vertexBuffer, vertexBufferSize) = WgpuFactory.CreateBufferWithData(gfx.WgpuDevice, Wgpu.BufferUsage.Vertex, new[] {
                new Vertex(Position: new Vector3(-0.5f, 0.5f, 0.0f), UV: new Vector2(0.0f, 0.0f)),
                new Vertex(Position: new Vector3(0.5f, 0.5f, 0.0f), UV: new Vector2(1.0f, 0.0f)),
                new Vertex(Position: new Vector3(0.5f, -0.5f, 0.0f), UV: new Vector2(1.0f, 1.0f)),
                new Vertex(Position: new Vector3(-0.5f, -0.5f, 0.0f), UV: new Vector2(0.0f, 1.0f)),
            });

            var (indexBuffer, indexBufferSize) = WgpuFactory.CreateBufferWithData(gfx.WgpuDevice, Wgpu.BufferUsage.Index, new short[] {
                0, 1, 2,
                0, 2, 3
            });

            Console.WriteLine("Starting pipeline creation");
            var renderPipeline = new PipelineBuilder<Vertex>()
                .WithShaderSource(File.ReadAllText("shader.wgsl"))
                .WithBindGroupLayout(textureBindGroupLayout)
                .Build(gfx);
            Console.WriteLine("Created a render pipeline\n");

            glfw.GetWindowSize(window, out int prevWidth, out int prevHeight);
            var swapChainFormat = Wgpu.SurfaceGetPreferredFormat(gfx.WgpuSurface, gfx.WgpuAdapter);
            var swapChainDescriptor = new Wgpu.SwapChainDescriptor()
            {
                usage = (uint)Wgpu.TextureUsage.RenderAttachment,
                format = swapChainFormat,
                width = (uint)prevWidth,
                height = (uint)prevHeight,
                presentMode = Wgpu.PresentMode.Fifo
            };

            var swapChain = Wgpu.DeviceCreateSwapChain(gfx.WgpuDevice, gfx.WgpuSurface, in swapChainDescriptor);

            while (!glfw.WindowShouldClose(window))
            {
                glfw.GetWindowSize(window, out int width, out int height);

                if (width != prevWidth || height != prevHeight)
                {
                    prevWidth = width;
                    prevHeight = height;
                    swapChainDescriptor.width = (uint)width;
                    swapChainDescriptor.height = (uint)height;
                    swapChain = Wgpu.DeviceCreateSwapChain(gfx.WgpuDevice, gfx.WgpuSurface, in swapChainDescriptor);
                }

                var nextTexture = Wgpu.SwapChainGetCurrentTextureView(swapChain);
                if (nextTexture.Handle == IntPtr.Zero)
                    throw new Exception("Could not acquire next swap chain texture");

                var encoderDescriptor = new Wgpu.CommandEncoderDescriptor() { };
                var encoder = Wgpu.DeviceCreateCommandEncoder(gfx.WgpuDevice, in encoderDescriptor);

                var colorAttachment = new Wgpu.RenderPassColorAttachment()
                {
                    view = nextTexture,
                    resolveTarget = default,
                    loadOp = Wgpu.LoadOp.Clear,
                    storeOp = Wgpu.StoreOp.Store,
                    clearColor = new Wgpu.Color() { r = 0.25, g = 0.25, b = 0.25, a = 1 }
                };

                var renderPassDescriptor = new Wgpu.RenderPassDescriptor()
                {
                    colorAttachments = (IntPtr)(&colorAttachment),
                    colorAttachmentCount = 1
                };

                var renderPass = Wgpu.CommandEncoderBeginRenderPass(encoder, in renderPassDescriptor);

                Wgpu.RenderPassEncoderSetPipeline(renderPass, renderPipeline);
                var dynamicOffsets = 0u;
                Wgpu.RenderPassEncoderSetBindGroup(renderPass, 0, diffuseBindGroup, 0, ref dynamicOffsets);
                Wgpu.RenderPassEncoderSetVertexBuffer(renderPass, 0, vertexBuffer, 0, (ulong)vertexBufferSize);
                Wgpu.RenderPassEncoderSetIndexBuffer(renderPass, indexBuffer, Wgpu.IndexFormat.Uint16, 0, (ulong)indexBufferSize);
                Wgpu.RenderPassEncoderDrawIndexed(renderPass, 6, 1, 0, 0, 0);
                Wgpu.RenderPassEncoderEndPass(renderPass);

                var queue = Wgpu.DeviceGetQueue(gfx.WgpuDevice);
                var commandBufferDescriptor = new Wgpu.CommandBufferDescriptor();
                var commandBuffer = Wgpu.CommandEncoderFinish(encoder, in commandBufferDescriptor);

                Wgpu.QueueSubmit(queue, 1, ref commandBuffer);
                Wgpu.SwapChainPresent(swapChain);

                glfw.PollEvents();
            }

            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }
}