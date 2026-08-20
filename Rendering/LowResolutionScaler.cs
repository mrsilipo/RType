using Microsoft.Xna.Framework;

namespace RetroRacer.Rendering;

public static class LowResolutionScaler
{
    public static Rectangle GetDestinationRectangle(int backBufferWidth, int backBufferHeight, int sourceWidth, int sourceHeight)
    {
        int integerScale = Math.Min(backBufferWidth / sourceWidth, backBufferHeight / sourceHeight);
        if (integerScale >= 1)
        {
            int width = sourceWidth * integerScale;
            int height = sourceHeight * integerScale;
            return new Rectangle((backBufferWidth - width) / 2, (backBufferHeight - height) / 2, width, height);
        }

        float scale = MathF.Min(backBufferWidth / (float)sourceWidth, backBufferHeight / (float)sourceHeight);
        int scaledWidth = Math.Max(1, (int)MathF.Floor(sourceWidth * scale));
        int scaledHeight = Math.Max(1, (int)MathF.Floor(sourceHeight * scale));
        return new Rectangle((backBufferWidth - scaledWidth) / 2, (backBufferHeight - scaledHeight) / 2, scaledWidth, scaledHeight);
    }
}
