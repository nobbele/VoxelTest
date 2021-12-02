using WGPU.NET;

namespace VoxelTest.Graphics
{
    public interface IVertexFieldType
    {
        protected Wgpu.VertexFormat VertexFormat { get; }
    }
}