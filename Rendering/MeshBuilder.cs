using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RType.Rendering;

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

    public void AddCylinderX(Vector3 center, float radius, float width, int sides)
    {
        int safeSides = Math.Max(6, sides);
        float halfWidth = width * 0.5f;
        int sideStart = _vertices.Count;
        for (int i = 0; i <= safeSides; i++)
        {
            float angle = MathF.Tau * i / safeSides;
            float y = MathF.Cos(angle) * radius;
            float z = MathF.Sin(angle) * radius;
            Vector3 normal = Vector3.Normalize(new Vector3(0f, y, z));
            _vertices.Add(new VertexPositionNormalTexture(center + new Vector3(-halfWidth, y, z), normal, new Vector2(i / (float)safeSides, 0f)));
            _vertices.Add(new VertexPositionNormalTexture(center + new Vector3(halfWidth, y, z), normal, new Vector2(i / (float)safeSides, 1f)));
        }

        for (int i = 0; i < safeSides; i++)
        {
            int a = sideStart + i * 2;
            int b = a + 1;
            int c = a + 2;
            int d = a + 3;
            _indices.Add(a);
            _indices.Add(b);
            _indices.Add(c);
            _indices.Add(b);
            _indices.Add(d);
            _indices.Add(c);
        }

        AddCylinderCapX(center, -halfWidth, radius, safeSides, Vector3.Left);
        AddCylinderCapX(center, halfWidth, radius, safeSides, Vector3.Right);
    }

    private void AddCylinderCapX(Vector3 center, float xOffset, float radius, int sides, Vector3 normal)
    {
        int centerIndex = _vertices.Count;
        _vertices.Add(new VertexPositionNormalTexture(center + new Vector3(xOffset, 0f, 0f), normal, new Vector2(0.5f, 0.5f)));
        int ringStart = _vertices.Count;
        for (int i = 0; i < sides; i++)
        {
            float angle = MathF.Tau * i / sides;
            float y = MathF.Cos(angle) * radius;
            float z = MathF.Sin(angle) * radius;
            _vertices.Add(new VertexPositionNormalTexture(
                center + new Vector3(xOffset, y, z),
                normal,
                new Vector2(0.5f + y / MathF.Max(0.001f, radius * 2f), 0.5f + z / MathF.Max(0.001f, radius * 2f))));
        }

        for (int i = 0; i < sides; i++)
        {
            int current = ringStart + i;
            int next = ringStart + (i + 1) % sides;
            if (normal.X > 0f)
            {
                _indices.Add(centerIndex);
                _indices.Add(current);
                _indices.Add(next);
            }
            else
            {
                _indices.Add(centerIndex);
                _indices.Add(next);
                _indices.Add(current);
            }
        }
    }

    public StaticMesh Build(
        GraphicsDevice graphicsDevice,
        string name,
        Texture2D texture,
        Vector3 diffuseColor,
        bool isWheelMesh = false,
        VehicleMaterial? vehicleMaterial = null,
        WheelCorner wheelCorner = WheelCorner.None,
        Vector3? localPivot = null)
    {
        return new StaticMesh(
            graphicsDevice,
            name,
            _vertices.ToArray(),
            _indices.ToArray(),
            texture,
            diffuseColor,
            isWheelMesh,
            vehicleMaterial: vehicleMaterial,
            wheelCorner: wheelCorner,
            localPivot: localPivot);
    }
}
