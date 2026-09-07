using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RType.Rendering;

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

    public static StaticMesh CreateGroundRectangle(
        GraphicsDevice graphicsDevice,
        Vector3 center,
        Vector2 axisA,
        float lengthA,
        Vector2 axisB,
        float lengthB,
        Texture2D texture,
        Vector3 diffuseColor,
        string name,
        float uvRepeatX = 1f,
        float uvRepeatY = 1f)
    {
        Vector2 safeAxisA = axisA.LengthSquared() <= 0.0001f ? Vector2.UnitX : Vector2.Normalize(axisA);
        Vector2 safeAxisB = axisB.LengthSquared() <= 0.0001f ? Vector2.UnitY : Vector2.Normalize(axisB);
        Vector3 halfA = new Vector3(safeAxisA.X, 0f, safeAxisA.Y) * (lengthA * 0.5f);
        Vector3 halfB = new Vector3(safeAxisB.X, 0f, safeAxisB.Y) * (lengthB * 0.5f);
        MeshBuilder builder = new();
        builder.AddQuad(
            center - halfA - halfB,
            center + halfA - halfB,
            center + halfA + halfB,
            center - halfA + halfB,
            new Vector2(0f, uvRepeatY),
            new Vector2(uvRepeatX, uvRepeatY),
            new Vector2(uvRepeatX, 0f),
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

    public static StaticMesh CreateBox(
        GraphicsDevice graphicsDevice,
        Vector3 center,
        Vector3 size,
        Texture2D texture,
        Vector3 diffuseColor,
        string name,
        VehicleMaterial? vehicleMaterial = null)
    {
        MeshBuilder builder = new();
        builder.AddBox(center, size);
        return builder.Build(graphicsDevice, name, texture, diffuseColor, vehicleMaterial: vehicleMaterial);
    }

    public static StaticMesh CreateCylinderY(
        GraphicsDevice graphicsDevice,
        Vector3 center,
        float radius,
        float height,
        int sides,
        Texture2D texture,
        Vector3 diffuseColor,
        string name,
        VehicleMaterial? vehicleMaterial = null)
    {
        MeshBuilder builder = new();
        builder.AddCylinderY(center, radius, height, sides);
        return builder.Build(graphicsDevice, name, texture, diffuseColor, vehicleMaterial: vehicleMaterial);
    }

    public static StaticMesh CreateCarWheelSet(GraphicsDevice graphicsDevice, Texture2D texture)
    {
        MeshBuilder builder = new();
        builder.AddBox(new Vector3(-0.82f, 0.28f, 1.34f), new Vector3(0.24f, 0.54f, 0.60f));
        builder.AddBox(new Vector3(0.82f, 0.28f, 1.34f), new Vector3(0.24f, 0.54f, 0.60f));
        builder.AddBox(new Vector3(-0.82f, 0.28f, -1.40f), new Vector3(0.24f, 0.54f, 0.60f));
        builder.AddBox(new Vector3(0.82f, 0.28f, -1.40f), new Vector3(0.24f, 0.54f, 0.60f));
        return builder.Build(
            graphicsDevice,
            "placeholder wheels",
            texture,
            Vector3.One,
            isWheelMesh: true,
            vehicleMaterial: VehicleMaterial.CreateDefault(VehicleMaterialCategory.TyreRubber));
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

    public static StaticMesh CreateVerticalPlane(
        GraphicsDevice graphicsDevice,
        Vector3 center,
        float width,
        float height,
        float yawRadians,
        Texture2D texture,
        Vector3 diffuseColor,
        string name,
        float alpha = 1f,
        float uvRepeatX = 1f,
        float uvRepeatY = 1f)
    {
        MeshBuilder builder = new();
        Vector3 across = Vector3.Normalize(new Vector3(MathF.Cos(yawRadians), 0f, MathF.Sin(yawRadians)));
        Vector3 normal = Vector3.Normalize(Vector3.Cross(across, Vector3.Up));
        Vector3 halfAcross = across * (width * 0.5f);
        Vector3 halfUp = Vector3.Up * (height * 0.5f);
        builder.AddQuad(
            center - halfAcross - halfUp,
            center + halfAcross - halfUp,
            center + halfAcross + halfUp,
            center - halfAcross + halfUp,
            new Vector2(0f, uvRepeatY),
            new Vector2(uvRepeatX, uvRepeatY),
            new Vector2(uvRepeatX, 0f),
            new Vector2(0f, 0f),
            normal);
        return builder.Build(graphicsDevice, name, texture, diffuseColor, alpha: alpha);
    }

    public static StaticMesh CreateVerticalRing(
        GraphicsDevice graphicsDevice,
        float radius,
        float baseY,
        float height,
        int segments,
        Texture2D texture,
        Vector3 diffuseColor,
        string name,
        float alpha = 1f,
        float uvRepeatX = 1f)
    {
        MeshBuilder builder = new();
        int safeSegments = Math.Max(16, segments);

        for (int i = 0; i < safeSegments; i++)
        {
            float a0 = MathF.Tau * i / safeSegments;
            float a1 = MathF.Tau * (i + 1) / safeSegments;
            Vector3 p0 = new(MathF.Sin(a0) * radius, baseY, MathF.Cos(a0) * radius);
            Vector3 p1 = new(MathF.Sin(a1) * radius, baseY, MathF.Cos(a1) * radius);
            Vector3 p2 = p1 + Vector3.Up * height;
            Vector3 p3 = p0 + Vector3.Up * height;
            float u0 = uvRepeatX * i / safeSegments;
            float u1 = uvRepeatX * (i + 1) / safeSegments;
            Vector3 normal = Vector3.Normalize(new Vector3(
                MathF.Sin((a0 + a1) * 0.5f),
                0f,
                MathF.Cos((a0 + a1) * 0.5f)));

            builder.AddQuad(
                p1,
                p0,
                p3,
                p2,
                new Vector2(u1, 1f),
                new Vector2(u0, 1f),
                new Vector2(u0, 0f),
                new Vector2(u1, 0f),
                normal);
        }

        return builder.Build(graphicsDevice, name, texture, diffuseColor, alpha: alpha);
    }

    public static StaticMesh CreateHorizonTerrainRing(
        GraphicsDevice graphicsDevice,
        float radius,
        float baseY,
        float minimumTopY,
        float maximumTopY,
        int segments,
        Texture2D texture,
        Vector3 diffuseColor,
        string name,
        float phase = 0f)
    {
        MeshBuilder builder = new();
        int safeSegments = Math.Max(24, segments);
        float[] topHeights = new float[safeSegments + 1];

        for (int i = 0; i <= safeSegments; i++)
        {
            float t = i / (float)safeSegments;
            float a = MathF.Tau * t;
            float ridge =
                MathF.Sin(a * 2.0f + phase) * 0.25f +
                MathF.Sin(a * 5.0f + phase * 0.63f + 1.2f) * 0.24f +
                MathF.Sin(a * 11.0f + phase * 1.47f + 0.5f) * 0.17f +
                MathF.Sin(a * 19.0f + phase * 0.28f + 2.3f) * 0.10f +
                MathF.Sin(a * 31.0f + phase * 0.91f + 0.8f) * 0.045f;
            ridge = MathHelper.Clamp(ridge * 0.5f + 0.5f, 0f, 1f);
            topHeights[i] = MathHelper.Lerp(minimumTopY, maximumTopY, ridge);
        }

        topHeights[safeSegments] = topHeights[0];

        for (int i = 0; i < safeSegments; i++)
        {
            float a0 = MathF.Tau * i / safeSegments;
            float a1 = MathF.Tau * (i + 1) / safeSegments;
            Vector3 p0 = new(MathF.Sin(a0) * radius, baseY, MathF.Cos(a0) * radius);
            Vector3 p1 = new(MathF.Sin(a1) * radius, baseY, MathF.Cos(a1) * radius);
            Vector3 p2 = new(MathF.Sin(a1) * radius, topHeights[i + 1], MathF.Cos(a1) * radius);
            Vector3 p3 = new(MathF.Sin(a0) * radius, topHeights[i], MathF.Cos(a0) * radius);
            Vector3 normal = Vector3.Normalize(new Vector3(
                MathF.Sin((a0 + a1) * 0.5f),
                0.16f,
                MathF.Cos((a0 + a1) * 0.5f)));

            builder.AddQuad(
                p1,
                p0,
                p3,
                p2,
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                normal);
        }

        return builder.Build(graphicsDevice, name, texture, diffuseColor);
    }

    public static StaticMesh CreateHorizonTerrainBelt(
        GraphicsDevice graphicsDevice,
        float innerRadius,
        float outerRadius,
        float innerY,
        float outerMinimumY,
        float outerMaximumY,
        int angularSegments,
        int radialSegments,
        Texture2D texture,
        Vector3 diffuseColor,
        string name,
        float phase = 0f)
    {
        MeshBuilder builder = new();
        int safeAngularSegments = Math.Max(24, angularSegments);
        int safeRadialSegments = Math.Max(1, radialSegments);

        for (int i = 0; i < safeAngularSegments; i++)
        {
            float a0 = MathF.Tau * i / safeAngularSegments;
            float a1 = MathF.Tau * (i + 1) / safeAngularSegments;
            float u0 = i / (float)safeAngularSegments * 12f;
            float u1 = (i + 1) / (float)safeAngularSegments * 12f;

            for (int r = 0; r < safeRadialSegments; r++)
            {
                float t0 = r / (float)safeRadialSegments;
                float t1 = (r + 1) / (float)safeRadialSegments;
                Vector3 p0 = TerrainBeltPoint(a0, t0, innerRadius, outerRadius, innerY, outerMinimumY, outerMaximumY, phase);
                Vector3 p1 = TerrainBeltPoint(a1, t0, innerRadius, outerRadius, innerY, outerMinimumY, outerMaximumY, phase);
                Vector3 p2 = TerrainBeltPoint(a1, t1, innerRadius, outerRadius, innerY, outerMinimumY, outerMaximumY, phase);
                Vector3 p3 = TerrainBeltPoint(a0, t1, innerRadius, outerRadius, innerY, outerMinimumY, outerMaximumY, phase);
                Vector3 normal = CalculateQuadNormal(p0, p1, p3);

                builder.AddQuad(
                    p0,
                    p1,
                    p2,
                    p3,
                    new Vector2(u0, t0),
                    new Vector2(u1, t0),
                    new Vector2(u1, t1),
                    new Vector2(u0, t1),
                    normal);
            }
        }

        return builder.Build(graphicsDevice, name, texture, diffuseColor);
    }

    public static StaticMesh CreateBrokenHorizonTerrainBelt(
        GraphicsDevice graphicsDevice,
        float innerRadius,
        float outerRadius,
        float innerY,
        float outerMinimumY,
        float outerMaximumY,
        int angularSegments,
        int radialSegments,
        Texture2D texture,
        Vector3 diffuseColor,
        string name,
        float phase = 0f,
        float density = 0.62f)
    {
        MeshBuilder builder = new();
        int safeAngularSegments = Math.Max(24, angularSegments);
        int safeRadialSegments = Math.Max(1, radialSegments);
        float safeDensity = MathHelper.Clamp(density, 0.05f, 1f);

        for (int i = 0; i < safeAngularSegments; i++)
        {
            float centerAngle = MathF.Tau * (i + 0.5f) / safeAngularSegments;
            float noise =
                MathF.Sin(centerAngle * 3.0f + phase) * 0.38f +
                MathF.Sin(centerAngle * 7.0f + phase * 0.71f + 1.3f) * 0.34f +
                MathF.Sin(centerAngle * 17.0f + phase * 1.17f + 0.4f) * 0.22f +
                MathF.Sin(centerAngle * 31.0f + phase * 0.31f + 2.1f) * 0.12f;
            float presence = MathHelper.Clamp(noise * 0.5f + 0.5f, 0f, 1f);
            if (presence > safeDensity)
            {
                continue;
            }

            float a0 = MathF.Tau * i / safeAngularSegments;
            float a1 = MathF.Tau * (i + 1) / safeAngularSegments;
            float clumpTopScale = MathHelper.Lerp(0.55f, 1.18f, 1f - presence / safeDensity);
            float u0 = i / (float)safeAngularSegments * 12f;
            float u1 = (i + 1) / (float)safeAngularSegments * 12f;

            for (int r = 0; r < safeRadialSegments; r++)
            {
                float t0 = r / (float)safeRadialSegments;
                float t1 = (r + 1) / (float)safeRadialSegments;
                Vector3 p0 = TerrainBeltPoint(a0, t0, innerRadius, outerRadius, innerY, outerMinimumY, outerMaximumY * clumpTopScale, phase);
                Vector3 p1 = TerrainBeltPoint(a1, t0, innerRadius, outerRadius, innerY, outerMinimumY, outerMaximumY * clumpTopScale, phase);
                Vector3 p2 = TerrainBeltPoint(a1, t1, innerRadius, outerRadius, innerY, outerMinimumY, outerMaximumY * clumpTopScale, phase);
                Vector3 p3 = TerrainBeltPoint(a0, t1, innerRadius, outerRadius, innerY, outerMinimumY, outerMaximumY * clumpTopScale, phase);
                Vector3 normal = CalculateQuadNormal(p0, p1, p3);

                builder.AddQuad(
                    p0,
                    p1,
                    p2,
                    p3,
                    new Vector2(u0, t0),
                    new Vector2(u1, t0),
                    new Vector2(u1, t1),
                    new Vector2(u0, t1),
                    normal);
            }
        }

        return builder.Build(graphicsDevice, name, texture, diffuseColor);
    }

    public static StaticMesh CreateRollingTerrainPatch(
        GraphicsDevice graphicsDevice,
        Vector3 center,
        float width,
        float depth,
        float height,
        int xSegments,
        int zSegments,
        Texture2D texture,
        float uvRepeat,
        Vector3 diffuseColor,
        string name,
        float phase = 0f)
    {
        MeshBuilder builder = new();
        int safeXSegments = Math.Max(1, xSegments);
        int safeZSegments = Math.Max(1, zSegments);

        for (int z = 0; z < safeZSegments; z++)
        {
            float z0 = z / (float)safeZSegments;
            float z1 = (z + 1) / (float)safeZSegments;
            for (int x = 0; x < safeXSegments; x++)
            {
                float x0 = x / (float)safeXSegments;
                float x1 = (x + 1) / (float)safeXSegments;
                Vector3 p0 = RollingTerrainPoint(center, width, depth, height, x0, z0, phase);
                Vector3 p1 = RollingTerrainPoint(center, width, depth, height, x1, z0, phase);
                Vector3 p2 = RollingTerrainPoint(center, width, depth, height, x1, z1, phase);
                Vector3 p3 = RollingTerrainPoint(center, width, depth, height, x0, z1, phase);
                Vector3 normal = CalculateQuadNormal(p0, p1, p3);

                builder.AddQuad(
                    p0,
                    p1,
                    p2,
                    p3,
                    new Vector2(x0 * uvRepeat, z0 * uvRepeat),
                    new Vector2(x1 * uvRepeat, z0 * uvRepeat),
                    new Vector2(x1 * uvRepeat, z1 * uvRepeat),
                    new Vector2(x0 * uvRepeat, z1 * uvRepeat),
                    normal);
            }
        }

        return builder.Build(graphicsDevice, name, texture, diffuseColor);
    }

    private static Vector3 RollingTerrainPoint(
        Vector3 center,
        float width,
        float depth,
        float height,
        float xT,
        float zT,
        float phase)
    {
        float x = (xT - 0.5f) * width;
        float z = (zT - 0.5f) * depth;
        float edgeFadeX = MathF.Sin(xT * MathF.PI);
        float edgeFadeZ = MathF.Sin(zT * MathF.PI);
        float edgeFade = MathHelper.Clamp(edgeFadeX * edgeFadeZ, 0f, 1f);
        float broad =
            MathF.Sin(xT * MathF.Tau * 1.15f + phase) * 0.48f +
            MathF.Sin(zT * MathF.Tau * 1.45f + phase * 0.71f) * 0.36f +
            MathF.Sin((xT + zT) * MathF.Tau * 0.82f + phase * 1.6f) * 0.22f;
        float y = center.Y + height * broad * edgeFade;
        return new Vector3(center.X + x, y, center.Z + z);
    }

    private static Vector3 TerrainBeltPoint(
        float angle,
        float radialT,
        float innerRadius,
        float outerRadius,
        float innerY,
        float outerMinimumY,
        float outerMaximumY,
        float phase)
    {
        float baseRadius = MathHelper.Lerp(innerRadius, outerRadius, radialT);
        float beltDepth = MathF.Max(0.001f, outerRadius - innerRadius);
        float radiusNoise =
            MathF.Sin(angle * 1.0f + phase * 0.73f) * 0.58f +
            MathF.Sin(angle * 2.7f + phase * 1.31f + 1.9f) * 0.36f +
            MathF.Sin(angle * 6.4f + phase * 0.47f + 0.6f) * 0.18f;
        float radialFade = MathHelper.Clamp(0.35f + radialT * 0.65f, 0f, 1f);
        float radius = baseRadius + radiusNoise * beltDepth * 0.085f * radialFade;
        float ridge =
            MathF.Sin(angle * 2.0f + phase) * 0.25f +
            MathF.Sin(angle * 5.0f + phase * 0.63f + 1.2f) * 0.24f +
            MathF.Sin(angle * 11.0f + phase * 1.47f + 0.5f) * 0.17f +
            MathF.Sin(angle * 19.0f + phase * 0.28f + 2.3f) * 0.10f +
            MathF.Sin(angle * 31.0f + phase * 0.91f + 0.8f) * 0.045f;
        ridge = MathHelper.Clamp(ridge * 0.5f + 0.5f, 0f, 1f);
        float outerY = MathHelper.Lerp(outerMinimumY, outerMaximumY, ridge);
        float heightT = SmoothStep(radialT);
        float ripple = MathF.Sin(angle * 13.0f + radialT * 5.0f + phase) * 0.55f * radialT * (1f - radialT);
        float y = MathHelper.Lerp(innerY, outerY, heightT) + ripple;
        return new Vector3(MathF.Sin(angle) * radius, y, MathF.Cos(angle) * radius);
    }

    private static float SmoothStep(float value)
    {
        value = MathHelper.Clamp(value, 0f, 1f);
        return value * value * (3f - 2f * value);
    }

    public static StaticMesh CreateCrossBillboard(
        GraphicsDevice graphicsDevice,
        Vector3 center,
        float width,
        float height,
        Texture2D texture,
        Vector3 diffuseColor,
        string name,
        float alpha = 1f)
    {
        MeshBuilder builder = new();
        AddBillboardQuad(builder, center, width, height, 0f);
        AddBillboardQuad(builder, center, width, height, MathHelper.PiOver2);
        return builder.Build(graphicsDevice, name, texture, diffuseColor, alpha: alpha);
    }

    private static void AddBillboardQuad(MeshBuilder builder, Vector3 center, float width, float height, float yawRadians)
    {
        Vector3 across = Vector3.Normalize(new Vector3(MathF.Cos(yawRadians), 0f, MathF.Sin(yawRadians)));
        Vector3 normal = Vector3.Normalize(Vector3.Cross(across, Vector3.Up));
        Vector3 halfAcross = across * (width * 0.5f);
        Vector3 bottom = center;
        Vector3 top = center + Vector3.Up * height;
        builder.AddQuad(
            bottom - halfAcross,
            bottom + halfAcross,
            top + halfAcross,
            top - halfAcross,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0f),
            new Vector2(0f, 0f),
            normal);
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
