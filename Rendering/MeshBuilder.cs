using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RetroRacer.Rendering;

internal sealed class MeshBuilder
{
    private readonly List<VertexPositionNormalTexture> _vertices = [];
    private readonly List<int> _indices = [];

    public void AddQuad(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC,
        Vector2 uvD,
        Vector3 normal)
    {
        int start = _vertices.Count;
        _vertices.Add(new VertexPositionNormalTexture(a, normal, uvA));
        _vertices.Add(new VertexPositionNormalTexture(b, normal, uvB));
        _vertices.Add(new VertexPositionNormalTexture(c, normal, uvC));
        _vertices.Add(new VertexPositionNormalTexture(d, normal, uvD));

        _indices.Add(start);
        _indices.Add(start + 1);
        _indices.Add(start + 2);
        _indices.Add(start);
        _indices.Add(start + 2);
        _indices.Add(start + 3);
    }

    public void AddBox(Vector3 center, Vector3 size, float uvScale = 1f)
    {
        Vector3 half = size * 0.5f;
        float minX = center.X - half.X;
        float maxX = center.X + half.X;
        float minY = center.Y - half.Y;
        float maxY = center.Y + half.Y;
        float minZ = center.Z - half.Z;
        float maxZ = center.Z + half.Z;

        Vector2 uv00 = new(0f, 0f);
        Vector2 uv10 = new(uvScale, 0f);
        Vector2 uv11 = new(uvScale, uvScale);
        Vector2 uv01 = new(0f, uvScale);

        AddQuad(new Vector3(minX, minY, maxZ), new Vector3(maxX, minY, maxZ), new Vector3(maxX, maxY, maxZ), new Vector3(minX, maxY, maxZ), uv00, uv10, uv11, uv01, Vector3.Forward);
        AddQuad(new Vector3(maxX, minY, minZ), new Vector3(minX, minY, minZ), new Vector3(minX, maxY, minZ), new Vector3(maxX, maxY, minZ), uv00, uv10, uv11, uv01, Vector3.Backward);
        AddQuad(new Vector3(maxX, minY, maxZ), new Vector3(maxX, minY, minZ), new Vector3(maxX, maxY, minZ), new Vector3(maxX, maxY, maxZ), uv00, uv10, uv11, uv01, Vector3.Right);
        AddQuad(new Vector3(minX, minY, minZ), new Vector3(minX, minY, maxZ), new Vector3(minX, maxY, maxZ), new Vector3(minX, maxY, minZ), uv00, uv10, uv11, uv01, Vector3.Left);
        AddQuad(new Vector3(minX, maxY, maxZ), new Vector3(maxX, maxY, maxZ), new Vector3(maxX, maxY, minZ), new Vector3(minX, maxY, minZ), uv00, uv10, uv11, uv01, Vector3.Up);
        AddQuad(new Vector3(minX, minY, minZ), new Vector3(maxX, minY, minZ), new Vector3(maxX, minY, maxZ), new Vector3(minX, minY, maxZ), uv00, uv10, uv11, uv01, Vector3.Down);
    }

    public StaticMesh Build(GraphicsDevice graphicsDevice, string name, Texture2D texture, Vector3 diffuseColor, bool isWheelMesh = false)
    {
        return new StaticMesh(graphicsDevice, name, _vertices.ToArray(), _indices.ToArray(), texture, diffuseColor, isWheelMesh);
    }
}
