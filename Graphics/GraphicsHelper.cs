using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace VoxelTest.Graphics
{
    public class GraphicsHelper
    {
        public static IntPtr MarshalAndBox<T>([DisallowNull] T structure)
        {
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(structure));
            Marshal.StructureToPtr(structure, ptr, false);
            return ptr;
        }
    }
}