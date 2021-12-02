using Silk.NET.GLFW;
using WGPU.NET;

namespace VoxelTest.Graphics
{
    public class GraphicsContext
    {
        public Wgpu.SurfaceImpl WgpuSurface { get; }
        public Wgpu.AdapterImpl WgpuAdapter { get; }
        public Wgpu.DeviceImpl WgpuDevice { get; }
        public Wgpu.QueueImpl WgpuQueue { get; }

        public GraphicsContext(in GlfwNativeWindow window)
        {
            var wgpuInstance = new Wgpu.InstanceImpl();
            Console.WriteLine("Using WGPU v{0:X}", Wgpu.GetVersion());
            WgpuSurface = WgpuFactory.CreateSurface(wgpuInstance, in window);
            WgpuAdapter = WgpuFactory.CreateAdapter(wgpuInstance, WgpuSurface);
            WgpuDevice = WgpuFactory.CreateDevice(WgpuAdapter);
            WgpuQueue = Wgpu.DeviceGetQueue(WgpuDevice);
            Console.WriteLine("Initialized the graphics system\n");
        }
    }
}