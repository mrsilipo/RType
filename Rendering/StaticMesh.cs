using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RetroRacer.Rendering;

public sealed class StaticMesh : IDisposable
{
    private readonly VertexBuffer _vertexBuffer;
    private readonly IndexBuffer _indexBuffer;
    private readonly int _vertexCount;
    private readonly int _primitiveCount;

    public StaticMesh(
        GraphicsDevice graphicsDevice,
        string name,
        VertexPositionNormalTexture[] vertices,
        int[] indices,
        Texture2D texture,
        Vector3 diffuseColor,
        bool isWheelMesh = false,
        float alpha = 1f,
        Vector3? specularColor = null,
        float specularPower = 16f,
        Vector3? emissiveColor = null)
    {
        Name = name;
        Texture = texture;
        DiffuseColor = diffuseColor;
        IsWheelMesh = isWheelMesh;
        Alpha = MathHelper.Clamp(alpha, 0f, 1f);
        SpecularColor = specularColor ?? Vector3.Zero;
        SpecularPower = MathF.Max(1f, specularPower);
        EmissiveColor = emissiveColor ?? Vector3.Zero;
        _vertexCount = vertices.Length;
        _primitiveCount = indices.Length / 3;

        _vertexBuffer = new VertexBuffer(
            graphicsDevice,
            typeof(VertexPositionNormalTexture),
            vertices.Length,
            BufferUsage.WriteOnly);
        _vertexBuffer.SetData(vertices);

        _indexBuffer = new IndexBuffer(
            graphicsDevice,
            IndexElementSize.ThirtyTwoBits,
            indices.Length,
            BufferUsage.WriteOnly);
        _indexBuffer.SetData(indices);
    }

    public string Name { get; }

    public Texture2D Texture { get; }

    public Vector3 DiffuseColor { get; }

    public float Alpha { get; }

    public Vector3 SpecularColor { get; }

    public float SpecularPower { get; }

    public Vector3 EmissiveColor { get; }

    public bool IsWheelMesh { get; }

    public bool IsTransparent => Alpha < 0.995f;

    public void Draw(GraphicsDevice graphicsDevice, BasicEffect effect, Matrix world)
    {
        effect.World = world;
        effect.Texture = Texture;
        effect.TextureEnabled = true;
        effect.DiffuseColor = DiffuseColor;
        effect.Alpha = Alpha;
        effect.SpecularColor = SpecularColor;
        effect.SpecularPower = SpecularPower;
        effect.EmissiveColor = EmissiveColor;

        graphicsDevice.SetVertexBuffer(_vertexBuffer);
        graphicsDevice.Indices = _indexBuffer;

        foreach (EffectPass pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            graphicsDevice.DrawIndexedPrimitives(
                PrimitiveType.TriangleList,
                0,
                0,
                _primitiveCount);
        }
    }

    public void Dispose()
    {
        _vertexBuffer.Dispose();
        _indexBuffer.Dispose();
    }
}
