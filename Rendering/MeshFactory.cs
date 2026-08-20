using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RetroRacer.Rendering;

public static class MeshFactory
{
    public static StaticMesh CreatePlane(GraphicsDevice graphicsDevice, float width, float depth, float y, Texture2D texture, float uvRepeat, string name)
    {
        return CreatePlane(
            graphicsDevice,
            new Vector3(0f, y, 0f),
            width,
            depth,
            texture,
            uvRepeat,
            Vector3.One,
            name);
    }

    public static StaticMesh CreatePlane(
        GraphicsDevice graphicsDevice,
        Vector3 center,
        float width,
        float depth,
        Texture2D texture,
        float uvRepeat,
        Vector3 diffuseColor,
        string name)
    {
        MeshBuilder builder = new();
        float halfWidth = width * 0.5f;
        float halfDepth = depth * 0.5f;
        builder.AddQuad(
            center + new Vector3(-halfWidth, 0f, -halfDepth),
            center + new Vector3(halfWidth, 0f, -halfDepth),
            center + new Vector3(halfWidth, 0f, halfDepth),
            center + new Vector3(-halfWidth, 0f, halfDepth),
            new Vector2(0f, uvRepeat),
            new Vector2(uvRepeat, uvRepeat),
            new Vector2(uvRepeat, 0f),
            new Vector2(0f, 0f),
            Vector3.Up);
        return builder.Build(graphicsDevice, name, texture, diffuseColor);
    }

    public static StaticMesh CreateOffsetRibbon(
        GraphicsDevice graphicsDevice,
        IReadOnlyList<Vector2> centerLine,
        float offsetA,
        float offsetB,
        float y,
        Texture2D texture,
        float metersPerTextureRepeat,
        string name)
    {
        MeshBuilder builder = new();
        float distance = 0f;

        for (int i = 0; i < centerLine.Count; i++)
        {
            int nextIndex = (i + 1) % centerLine.Count;
            Vector2 center0 = centerLine[i];
            Vector2 center1 = centerLine[nextIndex];
            Vector2 left0 = GetLeftNormal(centerLine, i);
            Vector2 left1 = GetLeftNormal(centerLine, nextIndex);

            Vector2 a0 = center0 + left0 * offsetA;
            Vector2 b0 = center0 + left0 * offsetB;
            Vector2 a1 = center1 + left1 * offsetA;
            Vector2 b1 = center1 + left1 * offsetB;

            float nextDistance = distance + Vector2.Distance(center0, center1);
            float v0 = distance / metersPerTextureRepeat;
            float v1 = nextDistance / metersPerTextureRepeat;

            builder.AddQuad(
                ToGround(a0, y),
                ToGround(a1, y),
                ToGround(b1, y),
                ToGround(b0, y),
                new Vector2(0f, v0),
                new Vector2(0f, v1),
                new Vector2(1f, v1),
                new Vector2(1f, v0),
                Vector3.Up);

            distance = nextDistance;
        }

        return builder.Build(graphicsDevice, name, texture, Vector3.One);
    }

    public static StaticMesh CreateOffsetRibbon(
        GraphicsDevice graphicsDevice,
        IReadOnlyList<Vector3> centerLine,
        float offsetA,
        float offsetB,
        float yOffset,
        Texture2D texture,
        float metersPerTextureRepeat,
        string name)
    {
        MeshBuilder builder = new();
        float distance = 0f;

        for (int i = 0; i < centerLine.Count; i++)
        {
            int nextIndex = (i + 1) % centerLine.Count;
            Vector3 center0 = centerLine[i];
            Vector3 center1 = centerLine[nextIndex];
            Vector2 left0 = GetLeftNormalXZ(centerLine, i);
            Vector2 left1 = GetLeftNormalXZ(centerLine, nextIndex);

            Vector3 a0 = OffsetPoint(center0, left0 * offsetA, yOffset);
            Vector3 b0 = OffsetPoint(center0, left0 * offsetB, yOffset);
            Vector3 a1 = OffsetPoint(center1, left1 * offsetA, yOffset);
            Vector3 b1 = OffsetPoint(center1, left1 * offsetB, yOffset);

            float nextDistance = distance + Vector2.Distance(ToXZ(center0), ToXZ(center1));
            float v0 = distance / metersPerTextureRepeat;
            float v1 = nextDistance / metersPerTextureRepeat;
            Vector3 normal = CalculateQuadNormal(a0, a1, b0);

            builder.AddQuad(
                a0,
                a1,
                b1,
                b0,
                new Vector2(0f, v0),
                new Vector2(0f, v1),
                new Vector2(1f, v1),
                new Vector2(1f, v0),
                normal);

            distance = nextDistance;
        }

        return builder.Build(graphicsDevice, name, texture, Vector3.One);
    }

    public static StaticMesh CreateOffsetRibbon(
        GraphicsDevice graphicsDevice,
        IReadOnlyList<Vector3> centerLine,
        IReadOnlyList<float> offsetA,
        IReadOnlyList<float> offsetB,
        float yOffset,
        Texture2D texture,
        float metersPerTextureRepeat,
        string name)
    {
        MeshBuilder builder = new();
        float distance = 0f;

        for (int i = 0; i < centerLine.Count; i++)
        {
            int nextIndex = (i + 1) % centerLine.Count;
            Vector3 center0 = centerLine[i];
            Vector3 center1 = centerLine[nextIndex];
            Vector2 left0 = GetLeftNormalXZ(centerLine, i);
            Vector2 left1 = GetLeftNormalXZ(centerLine, nextIndex);
            float offsetA0 = SampleOffset(offsetA, i);
            float offsetB0 = SampleOffset(offsetB, i);
            float offsetA1 = SampleOffset(offsetA, nextIndex);
            float offsetB1 = SampleOffset(offsetB, nextIndex);

            Vector3 a0 = OffsetPoint(center0, left0 * offsetA0, yOffset);
            Vector3 b0 = OffsetPoint(center0, left0 * offsetB0, yOffset);
            Vector3 a1 = OffsetPoint(center1, left1 * offsetA1, yOffset);
            Vector3 b1 = OffsetPoint(center1, left1 * offsetB1, yOffset);

            float nextDistance = distance + Vector2.Distance(ToXZ(center0), ToXZ(center1));
            float v0 = distance / metersPerTextureRepeat;
            float v1 = nextDistance / metersPerTextureRepeat;
            Vector3 normal = CalculateQuadNormal(a0, a1, b0);

            builder.AddQuad(
                a0,
                a1,
                b1,
                b0,
                new Vector2(0f, v0),
                new Vector2(0f, v1),
                new Vector2(1f, v1),
                new Vector2(1f, v0),
                normal);

            distance = nextDistance;
        }

        return builder.Build(graphicsDevice, name, texture, Vector3.One);
    }

    public static StaticMesh CreateBankedOffsetRibbon(
        GraphicsDevice graphicsDevice,
        IReadOnlyList<Vector3> centerLine,
        IReadOnlyList<float> bankRadians,
        float offsetA,
        float offsetB,
        float yOffset,
        Texture2D texture,
        float metersPerTextureRepeat,
        string name)
    {
        MeshBuilder builder = new();
        float distance = 0f;

        for (int i = 0; i < centerLine.Count; i++)
        {
            int nextIndex = (i + 1) % centerLine.Count;
            Vector3 center0 = centerLine[i];
            Vector3 center1 = centerLine[nextIndex];
            Vector2 left0 = GetLeftNormalXZ(centerLine, i);
            Vector2 left1 = GetLeftNormalXZ(centerLine, nextIndex);
            float bank0 = i < bankRadians.Count ? bankRadians[i] : 0f;
            float bank1 = nextIndex < bankRadians.Count ? bankRadians[nextIndex] : 0f;

            Vector3 a0 = OffsetBankedPoint(center0, left0 * offsetA, offsetA, yOffset, bank0);
            Vector3 b0 = OffsetBankedPoint(center0, left0 * offsetB, offsetB, yOffset, bank0);
            Vector3 a1 = OffsetBankedPoint(center1, left1 * offsetA, offsetA, yOffset, bank1);
            Vector3 b1 = OffsetBankedPoint(center1, left1 * offsetB, offsetB, yOffset, bank1);

            float nextDistance = distance + Vector2.Distance(ToXZ(center0), ToXZ(center1));
            float v0 = distance / metersPerTextureRepeat;
            float v1 = nextDistance / metersPerTextureRepeat;
            Vector3 normal = CalculateQuadNormal(a0, a1, b0);

            builder.AddQuad(
                a0,
                a1,
                b1,
                b0,
                new Vector2(0f, v0),
                new Vector2(0f, v1),
                new Vector2(1f, v1),
                new Vector2(1f, v0),
                normal);

            distance = nextDistance;
        }

        return builder.Build(graphicsDevice, name, texture, Vector3.One);
    }

    public static StaticMesh CreateBankedOffsetRibbon(
        GraphicsDevice graphicsDevice,
        IReadOnlyList<Vector3> centerLine,
        IReadOnlyList<float> bankRadians,
        IReadOnlyList<float> offsetA,
        IReadOnlyList<float> offsetB,
        float yOffset,
        Texture2D texture,
        float metersPerTextureRepeat,
        string name)
    {
        MeshBuilder builder = new();
        float distance = 0f;

        for (int i = 0; i < centerLine.Count; i++)
        {
            int nextIndex = (i + 1) % centerLine.Count;
            Vector3 center0 = centerLine[i];
            Vector3 center1 = centerLine[nextIndex];
            Vector2 left0 = GetLeftNormalXZ(centerLine, i);
            Vector2 left1 = GetLeftNormalXZ(centerLine, nextIndex);
            float bank0 = i < bankRadians.Count ? bankRadians[i] : 0f;
            float bank1 = nextIndex < bankRadians.Count ? bankRadians[nextIndex] : 0f;
            float offsetA0 = SampleOffset(offsetA, i);
            float offsetB0 = SampleOffset(offsetB, i);
            float offsetA1 = SampleOffset(offsetA, nextIndex);
            float offsetB1 = SampleOffset(offsetB, nextIndex);

            Vector3 a0 = OffsetBankedPoint(center0, left0 * offsetA0, offsetA0, yOffset, bank0);
            Vector3 b0 = OffsetBankedPoint(center0, left0 * offsetB0, offsetB0, yOffset, bank0);
            Vector3 a1 = OffsetBankedPoint(center1, left1 * offsetA1, offsetA1, yOffset, bank1);
            Vector3 b1 = OffsetBankedPoint(center1, left1 * offsetB1, offsetB1, yOffset, bank1);

            float nextDistance = distance + Vector2.Distance(ToXZ(center0), ToXZ(center1));
            float v0 = distance / metersPerTextureRepeat;
            float v1 = nextDistance / metersPerTextureRepeat;
            Vector3 normal = CalculateQuadNormal(a0, a1, b0);

            builder.AddQuad(
                a0,
                a1,
                b1,
                b0,
                new Vector2(0f, v0),
                new Vector2(0f, v1),
                new Vector2(1f, v1),
                new Vector2(1f, v0),
                normal);

            distance = nextDistance;
        }

        return builder.Build(graphicsDevice, name, texture, Vector3.One);
    }

    public static StaticMesh CreateOffsetWall(
        GraphicsDevice graphicsDevice,
        IReadOnlyList<Vector3> centerLine,
        float offset,
        float height,
        float yOffset,
        Texture2D texture,
        float metersPerTextureRepeat,
        Vector3 diffuseColor,
        string name)
    {
        MeshBuilder builder = new();
        float distance = 0f;
        float sideSign = MathF.Sign(offset);
        if (sideSign == 0f)
        {
            sideSign = 1f;
        }

        for (int i = 0; i < centerLine.Count; i++)
        {
            int nextIndex = (i + 1) % centerLine.Count;
            Vector3 center0 = centerLine[i];
            Vector3 center1 = centerLine[nextIndex];
            Vector2 left0 = GetLeftNormalXZ(centerLine, i);
            Vector2 left1 = GetLeftNormalXZ(centerLine, nextIndex);
            Vector3 base0 = OffsetPoint(center0, left0 * offset, yOffset);
            Vector3 base1 = OffsetPoint(center1, left1 * offset, yOffset);
            Vector3 top0 = base0 + Vector3.Up * height;
            Vector3 top1 = base1 + Vector3.Up * height;

            float nextDistance = distance + Vector2.Distance(ToXZ(center0), ToXZ(center1));
            float v0 = distance / metersPerTextureRepeat;
            float v1 = nextDistance / metersPerTextureRepeat;
            Vector2 normal2 = left0 + left1;
            if (normal2.LengthSquared() > 0.0001f)
            {
                normal2.Normalize();
            }
            else
            {
                normal2 = left0;
            }

            normal2 *= -sideSign;
            Vector3 normal = normal2.LengthSquared() <= 0.0001f
                ? Vector3.Forward
                : new Vector3(normal2.X, 0f, normal2.Y);

            builder.AddQuad(
                base0,
                base1,
                top1,
                top0,
                new Vector2(0f, v0),
                new Vector2(1f, v1),
                new Vector2(1f, v1 + height),
                new Vector2(0f, v0 + height),
                normal);

            distance = nextDistance;
        }

        return builder.Build(graphicsDevice, name, texture, diffuseColor);
    }

    public static StaticMesh CreateOffsetWall(
        GraphicsDevice graphicsDevice,
        IReadOnlyList<Vector3> centerLine,
        IReadOnlyList<float> offsets,
        float height,
        float yOffset,
        Texture2D texture,
        float metersPerTextureRepeat,
        Vector3 diffuseColor,
        string name)
    {
        MeshBuilder builder = new();
        float distance = 0f;

        for (int i = 0; i < centerLine.Count; i++)
        {
            int nextIndex = (i + 1) % centerLine.Count;
            Vector3 center0 = centerLine[i];
            Vector3 center1 = centerLine[nextIndex];
            Vector2 left0 = GetLeftNormalXZ(centerLine, i);
            Vector2 left1 = GetLeftNormalXZ(centerLine, nextIndex);
            float offset0 = SampleOffset(offsets, i);
            float offset1 = SampleOffset(offsets, nextIndex);
            float sideSign = MathF.Sign((offset0 + offset1) * 0.5f);
            if (sideSign == 0f)
            {
                sideSign = 1f;
            }

            Vector3 base0 = OffsetPoint(center0, left0 * offset0, yOffset);
            Vector3 base1 = OffsetPoint(center1, left1 * offset1, yOffset);
            Vector3 top0 = base0 + Vector3.Up * height;
            Vector3 top1 = base1 + Vector3.Up * height;

            float nextDistance = distance + Vector2.Distance(ToXZ(center0), ToXZ(center1));
            float v0 = distance / metersPerTextureRepeat;
            float v1 = nextDistance / metersPerTextureRepeat;
            Vector2 normal2 = left0 + left1;
            if (normal2.LengthSquared() > 0.0001f)
            {
                normal2.Normalize();
            }
            else
            {
                normal2 = left0;
            }

            normal2 *= -sideSign;
            Vector3 normal = normal2.LengthSquared() <= 0.0001f
                ? Vector3.Forward
                : new Vector3(normal2.X, 0f, normal2.Y);

            builder.AddQuad(
                base0,
                base1,
                top1,
                top0,
                new Vector2(0f, v0),
                new Vector2(1f, v1),
                new Vector2(1f, v1 + height),
                new Vector2(0f, v0 + height),
                normal);

            distance = nextDistance;
        }

        return builder.Build(graphicsDevice, name, texture, diffuseColor);
    }

    public static StaticMesh CreateBox(GraphicsDevice graphicsDevice, Vector3 center, Vector3 size, Texture2D texture, Vector3 diffuseColor, string name)
    {
        MeshBuilder builder = new();
        builder.AddBox(center, size);
        return builder.Build(graphicsDevice, name, texture, diffuseColor);
    }

    public static StaticMesh CreateCarWheelSet(GraphicsDevice graphicsDevice, Texture2D texture)
    {
        MeshBuilder builder = new();
        builder.AddBox(new Vector3(-0.82f, 0.28f, 1.34f), new Vector3(0.24f, 0.54f, 0.60f));
        builder.AddBox(new Vector3(0.82f, 0.28f, 1.34f), new Vector3(0.24f, 0.54f, 0.60f));
        builder.AddBox(new Vector3(-0.82f, 0.28f, -1.40f), new Vector3(0.24f, 0.54f, 0.60f));
        builder.AddBox(new Vector3(0.82f, 0.28f, -1.40f), new Vector3(0.24f, 0.54f, 0.60f));
        return builder.Build(graphicsDevice, "placeholder wheels", texture, Vector3.One, isWheelMesh: true);
    }

    public static StaticMesh CreateUnitGroundQuad(GraphicsDevice graphicsDevice, Texture2D texture, string name)
    {
        MeshBuilder builder = new();
        builder.AddQuad(
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, 0.5f),
            new Vector3(-0.5f, 0f, 0.5f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0f),
            new Vector2(0f, 0f),
            Vector3.Up);
        return builder.Build(graphicsDevice, name, texture, Vector3.One);
    }

    private static Vector3 ToGround(Vector2 value, float y)
    {
        return new Vector3(value.X, y, value.Y);
    }

    private static Vector2 ToXZ(Vector3 value)
    {
        return new Vector2(value.X, value.Z);
    }

    private static Vector3 OffsetPoint(Vector3 center, Vector2 offset, float yOffset)
    {
        return new Vector3(center.X + offset.X, center.Y + yOffset, center.Z + offset.Y);
    }

    private static Vector3 OffsetBankedPoint(Vector3 center, Vector2 offset, float lateralOffset, float yOffset, float bankRadians)
    {
        return new Vector3(
            center.X + offset.X,
            center.Y + yOffset + lateralOffset * MathF.Tan(bankRadians),
            center.Z + offset.Y);
    }

    private static float SampleOffset(IReadOnlyList<float> offsets, int index)
    {
        return offsets.Count == 0 ? 0f : offsets[Math.Clamp(index, 0, offsets.Count - 1)];
    }

    private static Vector3 CalculateQuadNormal(Vector3 a, Vector3 b, Vector3 d)
    {
        Vector3 normal = Vector3.Cross(d - a, b - a);
        if (normal.LengthSquared() <= 0.0001f)
        {
            return Vector3.Up;
        }

        normal.Normalize();
        return normal.Y < 0f ? -normal : normal;
    }

    private static Vector2 GetTangent(IReadOnlyList<Vector2> points, int index)
    {
        Vector2 previous = points[(index - 1 + points.Count) % points.Count];
        Vector2 next = points[(index + 1) % points.Count];
        Vector2 tangent = next - previous;
        return tangent.LengthSquared() <= 0.0001f ? Vector2.UnitY : Vector2.Normalize(tangent);
    }

    private static Vector2 GetLeftNormal(IReadOnlyList<Vector2> points, int index)
    {
        Vector2 tangent = GetTangent(points, index);
        return new Vector2(-tangent.Y, tangent.X);
    }

    private static Vector2 GetTangentXZ(IReadOnlyList<Vector3> points, int index)
    {
        Vector2 previous = ToXZ(points[(index - 1 + points.Count) % points.Count]);
        Vector2 next = ToXZ(points[(index + 1) % points.Count]);
        Vector2 tangent = next - previous;
        return tangent.LengthSquared() <= 0.0001f ? Vector2.UnitY : Vector2.Normalize(tangent);
    }

    private static Vector2 GetLeftNormalXZ(IReadOnlyList<Vector3> points, int index)
    {
        Vector2 tangent = GetTangentXZ(points, index);
        return new Vector2(-tangent.Y, tangent.X);
    }
}
