using System.Runtime.InteropServices;
using Silk.NET.GLFW;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using WGPU.NET;

namespace VoxelTest.Graphics
{
    public class WgpuFactory
    {
        public static Wgpu.SurfaceImpl CreateSurface(Wgpu.InstanceImpl wgpuInstance, in GlfwNativeWindow window)
        {
            if (window.X11 is (nint hdisplay, nuint hwindow))
            {
                var descriptorInfo = new Wgpu.SurfaceDescriptorFromXlib()
                {
                    window = (uint)hwindow,
                    display = hdisplay,
                    chain = new Wgpu.ChainedStruct() { sType = Wgpu.SType.SurfaceDescriptorFromXlib }
                };
                unsafe
                {
                    var descriptor = new Wgpu.SurfaceDescriptor()
                    {
                        nextInChain = (IntPtr)(&descriptorInfo)
                    };
                    return Wgpu.InstanceCreateSurface(wgpuInstance, in descriptor);
                }
            }

            throw new Exception("Unsupported window type");
        }

        public static Wgpu.AdapterImpl CreateAdapter(Wgpu.InstanceImpl wgpuInstance, Wgpu.SurfaceImpl surface)
        {
            var adapterOptions = new Wgpu.RequestAdapterOptions()
            {
                compatibleSurface = surface
            };
            Wgpu.AdapterImpl adapter = default;
            Wgpu.InstanceRequestAdapter(wgpuInstance, in adapterOptions, (s, a, m, u) => { adapter = a; }, IntPtr.Zero);
            return adapter;
        }

        public static Wgpu.DeviceImpl CreateDevice(Wgpu.AdapterImpl adapter)
        {
            IntPtr deviceExtrasPtr = GraphicsHelper.MarshalAndBox(new Wgpu.DeviceExtras()
            {
                chain = new Wgpu.ChainedStruct() { sType = (Wgpu.SType)Wgpu.NativeSType.STypeDeviceExtras },
                label = "Device"
            });

            var requiredLimits = new Wgpu.RequiredLimits()
            {
                limits = new Wgpu.Limits()
                {
                    maxBindGroups = 1
                }
            };

            unsafe
            {
                var deviceDescriptor = new Wgpu.DeviceDescriptor()
                {
                    nextInChain = deviceExtrasPtr,
                    requiredLimits = (IntPtr)(&requiredLimits)
                };

                Wgpu.DeviceImpl device = default;
                Wgpu.AdapterRequestDevice(adapter, in deviceDescriptor, (s, d, m, u) => { device = d; }, IntPtr.Zero);
                return device;
            }
        }

        public static (Wgpu.BufferImpl, int) CreateBufferWithData<T>(Wgpu.DeviceImpl device, Wgpu.BufferUsage usage, T[] data) where T : struct
        {
            var vertexBufferSize = Marshal.SizeOf<T>() * data.Length;
            var vertexBufferDescriptor = new Wgpu.BufferDescriptor()
            {
                label = "Vertex Buffer",
                size = (uint)vertexBufferSize,
                usage = (uint)usage,
                mappedAtCreation = true,
            };
            var vertexBuffer = Wgpu.DeviceCreateBuffer(device, in vertexBufferDescriptor);
            var vertexSlice = Wgpu.BufferGetMappedRange(vertexBuffer, 0, (uint)vertexBufferSize);
            unsafe
            {
                var vertexSpan = new Span<T>(vertexSlice.ToPointer(), vertexBufferSize);
                for (int i = 0; i < data.Length; i++)
                {
                    vertexSpan[i] = data[i];
                }
            }
            Wgpu.BufferUnmap(vertexBuffer);
            return (vertexBuffer, vertexBufferSize);
        }

        public static Wgpu.TextureImpl CreateTextureWithData(GraphicsContext gfx, Image<Rgba32> image)
        {
            Console.WriteLine($"Texture size: {image.Width}x{image.Height}");
            var textureSize = new Wgpu.Extent3D()
            {
                width = (uint)image.Width,
                height = (uint)image.Height,
                depthOrArrayLayers = 1,
            };
            var textureDescriptor = new Wgpu.TextureDescriptor()
            {
                size = textureSize,
                mipLevelCount = 1,
                sampleCount = 1,
                dimension = Wgpu.TextureDimension.TwoDimensions,
                format = Wgpu.TextureFormat.RGBA8UnormSrgb,
                usage = (uint)(Wgpu.TextureUsage.TextureBinding | Wgpu.TextureUsage.CopyDst),
            };
            var texture = Wgpu.DeviceCreateTexture(gfx.WgpuDevice, in textureDescriptor);
            var imageCopyTexture = new Wgpu.ImageCopyTexture()
            {
                texture = texture,
                mipLevel = 0,
                origin = new Wgpu.Origin3D() { x = 0, y = 0, z = 0 },
                aspect = Wgpu.TextureAspect.All,
            };
            var textureDataLayout = new Wgpu.TextureDataLayout()
            {
                offset = 0,
                bytesPerRow = (uint)image.Width * 4,
                rowsPerImage = (uint)image.Height,
            };

            var imageData = image.GetPixelMemoryGroup()[0];
            unsafe
            {
                using var imagePin = imageData.Pin();
                Wgpu.QueueWriteTexture(gfx.WgpuQueue, in imageCopyTexture, new IntPtr(imagePin.Pointer), (ulong)imageData.Length * 4, in textureDataLayout, in textureSize);
            }
            Console.WriteLine("Copied texture data");
            return texture;
        }

        public static (Wgpu.BindGroupImpl, Wgpu.BindGroupLayoutImpl) CreateBindGroup(GraphicsContext gfx, Wgpu.TextureImpl texture)
        {
            var textureViewDescriptor = new Wgpu.TextureViewDescriptor() { };
            var textureView = Wgpu.TextureCreateView(texture, in textureViewDescriptor);

            var samplerDescriptor = new Wgpu.SamplerDescriptor()
            {
                addressModeU = Wgpu.AddressMode.ClampToEdge,
                addressModeV = Wgpu.AddressMode.ClampToEdge,
                addressModeW = Wgpu.AddressMode.ClampToEdge,
                magFilter = Wgpu.FilterMode.Linear,
                minFilter = Wgpu.FilterMode.Nearest,
                mipmapFilter = Wgpu.FilterMode.Nearest,
            };
            var sampler = Wgpu.DeviceCreateSampler(gfx.WgpuDevice, in samplerDescriptor);

            var textureBindGroupLayoutEntries = Marshal.AllocHGlobal(Marshal.SizeOf<Wgpu.BindGroupLayoutEntry>() * 2);
            Marshal.StructureToPtr(new Wgpu.BindGroupLayoutEntry()
            {
                binding = 0,
                visibility = (uint)Wgpu.ShaderStage.Fragment,
                texture = new Wgpu.TextureBindingLayout()
                {
                    sampleType = Wgpu.TextureSampleType.Float,
                    viewDimension = Wgpu.TextureViewDimension.TwoDimensions,
                    multisampled = false,
                },

            }, textureBindGroupLayoutEntries, false);
            Marshal.StructureToPtr(new Wgpu.BindGroupLayoutEntry()
            {
                binding = 1,
                visibility = (uint)Wgpu.ShaderStage.Fragment,
                sampler = new Wgpu.SamplerBindingLayout()
                {
                    type = Wgpu.SamplerBindingType.Filtering,
                }
            }, textureBindGroupLayoutEntries + Marshal.SizeOf<Wgpu.BindGroupLayoutEntry>(), false);
            var textureBindGroupLayoutDescriptor = new Wgpu.BindGroupLayoutDescriptor()
            {
                entryCount = 2,
                entries = textureBindGroupLayoutEntries,
            };
            var textureBindGroupLayout = Wgpu.DeviceCreateBindGroupLayout(gfx.WgpuDevice, in textureBindGroupLayoutDescriptor);

            var bindGroupEntries = Marshal.AllocHGlobal(Marshal.SizeOf<Wgpu.BindGroupEntry>() * 2);
            Marshal.StructureToPtr(new Wgpu.BindGroupEntry()
            {
                binding = 0,
                textureView = textureView,
            }, bindGroupEntries, false);
            Marshal.StructureToPtr(new Wgpu.BindGroupEntry()
            {
                binding = 1,
                sampler = sampler,
            }, bindGroupEntries + Marshal.SizeOf<Wgpu.BindGroupEntry>(), false);
            var bindGroupDescriptor = new Wgpu.BindGroupDescriptor()
            {
                layout = textureBindGroupLayout,
                entryCount = 2,
                entries = bindGroupEntries,
            };
            var bindGroup = Wgpu.DeviceCreateBindGroup(gfx.WgpuDevice, in bindGroupDescriptor);
            Marshal.FreeHGlobal(bindGroupEntries);
            Marshal.FreeHGlobal(textureBindGroupLayoutEntries);
            return (bindGroup, textureBindGroupLayout);
        }
    }
}