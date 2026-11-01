using System.Numerics;
using SkiaSharp;

internal static class Reference
{
    internal static Vector4 Filter(SKBitmap image, Vector2 uv) => Sample(image, uv);
    internal static Vector4 Blur(SKBitmap image, Vector2 uv, float level)
    {
        var color = (Sample(image, uv) + Sample(image, uv + new Vector2(level)) + Sample(image, uv - new Vector2(level))) / 3f;
        return color - new Vector4(level * 50, level * 50, level * 50, 0);
    }
    internal static Vector4 Warp(SKBitmap image, Vector2 uv, float x, float y, float time, float radius, int key)
    {
        float destX = uv.X, destY = uv.Y;
        float dx = 0, dy = 0, dist2 = 0;
        var centers = new[] {
            new Vector2(x + 20 * MathF.Cos(time) / 640, y + 30 * MathF.Sin(1.5f * time) / 640),
            new Vector2(x + 20 * MathF.Cos(1.5f * time) / 640, y + 30 * MathF.Sin(time) / 640),
            new Vector2(x, y)
        };
        for (int i = 0; i < centers.Length; i++)
        {
            dx = uv.X - centers[i].X; dy = (uv.Y - centers[i].Y) * 3 / 4;
            dist2 = dx * dx + dy * dy;
            if (dist2 >= radius * radius) continue;
            float k = (i == 2 ? 0.25f : 0.3f) * (MathF.Acos(MathF.Sqrt(dist2) / radius - 1) - MathF.Acos(0));
            destX -= dx * k; destY -= dy * k / 3 * 4;
        }
        var color = Sample(image, new Vector2(destX, destY));
        float r = radius + 0.02f;
        if (dist2 >= r * r) return color;
        float amount = 10 * (r * r - dist2);
        switch (key)
        {
            case 0: color.X -= amount; break;
            case 1: color.Y -= amount; break;
            case 2: color.Z -= amount; break;
            case 3: color.X -= amount; color.Y -= amount; break;
            case 4: color.X -= amount; color.Z -= amount; break;
            case 5: color.Z -= amount; color.Y -= amount; break;
        }
        return color;
    }
    internal static Vector4 Sample(SKBitmap image, Vector2 uv)
    {
        float px = uv.X * image.Width - 0.5f, py = uv.Y * image.Height - 0.5f;
        int x = (int)MathF.Floor(px), y = (int)MathF.Floor(py);
        float fx = px - x, fy = py - y;
        Vector4 Pixel(int ix, int iy)
        {
            var pixel = image.GetPixel(Mirror(ix, image.Width), Mirror(iy, image.Height));
            return new Vector4(pixel.Red, pixel.Green, pixel.Blue, pixel.Alpha) / 255f;
        }
        return Vector4.Lerp(Vector4.Lerp(Pixel(x, y), Pixel(x + 1, y), fx),
            Vector4.Lerp(Pixel(x, y + 1), Pixel(x + 1, y + 1), fx), fy);
    }
    private static int Mirror(int value, int size)
    {
        int wrapped = ((value % (2 * size)) + 2 * size) % (2 * size);
        return wrapped < size ? wrapped : 2 * size - wrapped - 1;
    }
}
