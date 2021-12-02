using System.Reflection;
using System.Runtime.InteropServices;
using WGPU.NET;

namespace VoxelTest.Graphics
{
    public class PipelineBuilder<T>
    {
        private readonly static List<(Wgpu.VertexFormat, int)> _vertexFieldFormats;
        private readonly static int _vertexSize;

        static PipelineBuilder()
        {
            _vertexFieldFormats = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Select(vertexField =>
            {
                var type = vertexField.FieldType;
                if (!type.IsAssignableTo(typeof(IVertexFieldType)))
                    throw new Exception("Invalid vertex field type");
                var vertexFormatGetter = type.GetInterface(nameof(IVertexFieldType))?
                    .GetProperty("VertexFormat", BindingFlags.Instance | BindingFlags.NonPublic)?
                    .GetMethod!;
                var vertexFormat = vertexFormatGetter.Invoke(Activator.CreateInstance(type), null)!;
                return ((Wgpu.VertexFormat)vertexFormat, Marshal.SizeOf(type));
            }).ToList();
            _vertexSize = Marshal.SizeOf<T>();
        }

        private Wgpu.ShaderModuleDescriptor? _shaderModuleDescriptor;
        private readonly List<Wgpu.BindGroupLayoutImpl> _bindGroupLayouts = new();

        public PipelineBuilder() { }

        public PipelineBuilder<T> WithShaderSource(string shaderSource)
        {
            var wgslDescriptor = GraphicsHelper.MarshalAndBox(new Wgpu.ShaderModuleWGSLDescriptor()
            {
                chain = new Wgpu.ChainedStruct() { sType = Wgpu.SType.ShaderModuleWGSLDescriptor },
                source = shaderSource
            });
            _shaderModuleDescriptor = new Wgpu.ShaderModuleDescriptor() { nextInChain = wgslDescriptor };
            return this;
        }

        public PipelineBuilder<T> WithBindGroupLayout(Wgpu.BindGroupLayoutImpl bindGroupLayout)
        {
            _bindGroupLayouts.Add(bindGroupLayout);
            return this;
        }

        public Wgpu.RenderPipelineImpl Build(GraphicsContext gfx)
        {
            if (_shaderModuleDescriptor is null)
                throw new Exception("Shader Module Descriptor is null");
            var shaderModuleDescriptor = (Wgpu.ShaderModuleDescriptor)_shaderModuleDescriptor;

            IntPtr vertexAttributesPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Wgpu.VertexAttribute>() * _vertexFieldFormats.Count);
            var offset = 0;
            int index = 0;
            foreach (var (vertexFieldFormat, vertexFieldSize) in _vertexFieldFormats)
            {
                Console.WriteLine($"Vertex data field format: {vertexFieldFormat}");
                Marshal.StructureToPtr(new Wgpu.VertexAttribute()
                {
                    offset = (ulong)offset,
                    shaderLocation = (uint)index,
                    format = vertexFieldFormat,
                }, vertexAttributesPtr + Marshal.SizeOf<Wgpu.VertexAttribute>() * index, false);
                offset += vertexFieldSize;
                index += 1;
            }

            var vertexBufferLayout = new Wgpu.VertexBufferLayout()
            {
                arrayStride = (ulong)_vertexSize,
                stepMode = Wgpu.VertexStepMode.Vertex,
                attributeCount = (uint)_vertexFieldFormats.Count,
                attributes = vertexAttributesPtr,
            };

            var shaderModule = Wgpu.DeviceCreateShaderModule(gfx.WgpuDevice, in shaderModuleDescriptor);
            var swapChainFormat = Wgpu.SurfaceGetPreferredFormat(gfx.WgpuSurface, gfx.WgpuAdapter);
            var blendState = new Wgpu.BlendState()
            {
                color = new Wgpu.BlendComponent()
                {
                    srcFactor = Wgpu.BlendFactor.One,
                    dstFactor = Wgpu.BlendFactor.Zero,
                    operation = Wgpu.BlendOperation.Add
                },
                alpha = new Wgpu.BlendComponent()
                {
                    srcFactor = Wgpu.BlendFactor.One,
                    dstFactor = Wgpu.BlendFactor.Zero,
                    operation = Wgpu.BlendOperation.Add
                }
            };
            var blendStateHandle = GCHandle.Alloc(blendState, GCHandleType.Pinned);

            var colorTargetState = new Wgpu.ColorTargetState()
            {
                format = swapChainFormat,
                blend = blendStateHandle.AddrOfPinnedObject(),
                writeMask = (uint)Wgpu.ColorWriteMask.All
            };
            var colorTargetStateHandle = GCHandle.Alloc(colorTargetState, GCHandleType.Pinned);
            var fragmentState = GraphicsHelper.MarshalAndBox(new Wgpu.FragmentState()
            {
                module = shaderModule,
                entryPoint = "fs_main",
                targetCount = 1,
                targets = colorTargetStateHandle.AddrOfPinnedObject(),
            });

            var bindGroupLayouts = Marshal.AllocHGlobal(Marshal.SizeOf<IntPtr>() * _bindGroupLayouts.Count);
            index = 0;
            foreach (var bindGroupLayout in _bindGroupLayouts)
            {
                Marshal.StructureToPtr(bindGroupLayout.Handle, bindGroupLayouts + Marshal.SizeOf<IntPtr>() * index, false);
            }
            var pipelineLayoutDescriptor = new Wgpu.PipelineLayoutDescriptor()
            {
                bindGroupLayoutCount = (uint)_bindGroupLayouts.Count,
                bindGroupLayouts = bindGroupLayouts,
            };
            var pipelineLayout = Wgpu.DeviceCreatePipelineLayout(gfx.WgpuDevice, in pipelineLayoutDescriptor);

            var renderPipelineVertexBuffersPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Wgpu.VertexBufferLayout>());
            Marshal.StructureToPtr(vertexBufferLayout, renderPipelineVertexBuffersPtr, false);
            var renderPipelineDescriptor = new Wgpu.RenderPipelineDescriptor()
            {
                label = "Render Pipeline",
                layout = pipelineLayout,
                vertex = new Wgpu.VertexState()
                {
                    module = shaderModule,
                    entryPoint = "vs_main",
                    bufferCount = 1,
                    buffers = renderPipelineVertexBuffersPtr,
                },
                primitive = new Wgpu.PrimitiveState()
                {
                    topology = Wgpu.PrimitiveTopology.TriangleList,
                    stripIndexFormat = Wgpu.IndexFormat.Undefined,
                    frontFace = Wgpu.FrontFace.CCW,
                    cullMode = Wgpu.CullMode.None
                },
                multisample = new Wgpu.MultisampleState()
                {
                    count = 1,
                    mask = uint.MaxValue,
                    alphaToCoverageEnabled = false
                },
                fragment = fragmentState
            };
            var renderPipeline = Wgpu.DeviceCreateRenderPipeline(gfx.WgpuDevice, in renderPipelineDescriptor);

            Marshal.FreeHGlobal(renderPipelineVertexBuffersPtr);
            colorTargetStateHandle.Free();
            blendStateHandle.Free();
            Marshal.FreeHGlobal(vertexAttributesPtr);
            return renderPipeline;
        }
    }
}