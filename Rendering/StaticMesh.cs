using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RType.Rendering;

public sealed class StaticMesh : IDisposable
{
    private readonly VertexBuffer _vertexBuffer;
    private readonly IndexBuffer _indexBuffer;
    private readonly VertexPositionNormalTexture[] _vertices;
    private readonly int[] _indices;
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
        Vector3? emissiveColor = null,
        VehicleMaterial? vehicleMaterial = null,
        WheelCorner wheelCorner = WheelCorner.None,
        Vector3? localPivot = null)
    {
        Name = name;
        Texture = texture;
        IsWheelMesh = isWheelMesh;
        WheelCorner = wheelCorner;
        LocalPivot = localPivot ?? Vector3.Zero;
        VehicleMaterial = vehicleMaterial;
        DiffuseColor = vehicleMaterial?.BaseColor ?? diffuseColor;
        Alpha = vehicleMaterial?.Opacity ?? MathHelper.Clamp(alpha, 0f, 1f);
        SpecularColor = vehicleMaterial?.ToBasicEffectSpecularColor() ?? specularColor ?? Vector3.Zero;
        SpecularPower = vehicleMaterial?.ToBasicEffectSpecularPower() ?? MathF.Max(1f, specularPower);
        EmissiveColor = vehicleMaterial?.ToBasicEffectEmissiveColor() ?? emissiveColor ?? Vector3.Zero;
        _vertices = vertices.ToArray();
        _indices = indices.ToArray();
        Bounds = CalculateBounds(vertices);
        BoundingSphere = BoundingSphere.CreateFromBoundingBox(Bounds);
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

    public WheelCorner WheelCorner { get; }

    public Vector3 LocalPivot { get; }

    public VehicleMaterial? VehicleMaterial { get; }

    public BoundingBox Bounds { get; }

    public BoundingSphere BoundingSphere { get; }

    public bool IsTransparent => Alpha < 0.995f;

    public IReadOnlyList<VertexPositionNormalTexture> Vertices => _vertices;

    public IReadOnlyList<int> Indices => _indices;

    public void Draw(GraphicsDevice graphicsDevice, BasicEffect effect, Matrix world)
    {
        Draw(graphicsDevice, effect, world, null);
    }

    public void Draw(GraphicsDevice graphicsDevice, BasicEffect effect, Matrix world, VehicleMaterial? materialOverride)
    {
        Draw(graphicsDevice, effect, world, materialOverride, forceTextureEnabled: true);
    }

    public void DrawUntextured(GraphicsDevice graphicsDevice, BasicEffect effect, Matrix world, VehicleMaterial? materialOverride)
    {
        Draw(graphicsDevice, effect, world, materialOverride, forceTextureEnabled: false);
    }

    private void Draw(
        GraphicsDevice graphicsDevice,
        BasicEffect effect,
        Matrix world,
        VehicleMaterial? materialOverride,
        bool forceTextureEnabled)
    {
        VehicleMaterial? material = materialOverride ?? VehicleMaterial;
        effect.World = world;
        effect.Texture = Texture;
        effect.TextureEnabled = forceTextureEnabled;
        if (material is VehicleMaterial vehicleMaterial)
        {
            effect.DiffuseColor = vehicleMaterial.ToBasicEffectDiffuseColor();
            effect.Alpha = vehicleMaterial.Opacity;
            effect.SpecularColor = vehicleMaterial.ToBasicEffectSpecularColor();
            effect.SpecularPower = vehicleMaterial.ToBasicEffectSpecularPower();
            effect.EmissiveColor = vehicleMaterial.ToBasicEffectEmissiveColor();
        }
        else
        {
            effect.DiffuseColor = DiffuseColor;
            effect.Alpha = Alpha;
            effect.SpecularColor = SpecularColor;
            effect.SpecularPower = SpecularPower;
            effect.EmissiveColor = EmissiveColor;
        }

        DrawIndexed(graphicsDevice, effect.CurrentTechnique.Passes);
    }

    public void Draw(GraphicsDevice graphicsDevice, Effect effect)
    {
        DrawIndexed(graphicsDevice, effect.CurrentTechnique.Passes);
    }

    public void Dispose()
    {
        _vertexBuffer.Dispose();
        _indexBuffer.Dispose();
    }

    private void DrawIndexed(GraphicsDevice graphicsDevice, EffectPassCollection passes)
    {
        graphicsDevice.SetVertexBuffer(_vertexBuffer);
        graphicsDevice.Indices = _indexBuffer;

        foreach (EffectPass pass in passes)
        {
            pass.Apply();
            graphicsDevice.DrawIndexedPrimitives(
                PrimitiveType.TriangleList,
                0,
                0,
                _primitiveCount);
        }
    }

    private static BoundingBox CalculateBounds(VertexPositionNormalTexture[] vertices)
    {
        if (vertices.Length == 0)
        {
            return new BoundingBox(Vector3.Zero, Vector3.Zero);
        }

        Vector3 min = vertices[0].Position;
        Vector3 max = vertices[0].Position;
        for (int i = 1; i < vertices.Length; i++)
        {
            Vector3 position = vertices[i].Position;
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }

        return new BoundingBox(min, max);
    }
}

public enum WheelCorner
{
    None,
    FrontLeft,
    FrontRight,
    RearLeft,
    RearRight
}
