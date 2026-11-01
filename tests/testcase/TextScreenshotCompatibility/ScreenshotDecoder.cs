using System.Buffers.Binary;
using SkiaSharp;
using THSSS.Imaging;
using SlimDX.Direct3D9;

internal static class ScreenshotDecoder
{
    internal static SKBitmap Decode(string path, ImageFileFormat format)
    {
        if (format is not (ImageFileFormat.Ppm or ImageFileFormat.Tga))
            return RasterCodec.Decode(path, SKColorType.Rgba8888);
        byte[] bytes = File.ReadAllBytes(path);
        int width, height, offset;
        if (format == ImageFileFormat.Tga)
        {
            if (bytes.Length < 18 || bytes[0] != 0 || bytes[1] != 0 || bytes[2] != 2 || bytes[16] != 32 || bytes[17] != 0x28)
                throw new InvalidDataException("Screenshot TGA must be uncompressed BGRA32 with top-down rows and 8 alpha bits");
            width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12));
            height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14));
            offset = 18;
        }
        else
        {
            int line1 = Array.IndexOf(bytes, (byte)'\n');
            int line2 = Array.IndexOf(bytes, (byte)'\n', line1 + 1);
            int line3 = Array.IndexOf(bytes, (byte)'\n', line2 + 1);
            if (line1 != 2 || bytes[0] != 'P' || bytes[1] != '6' || line2 < 0 || line3 < 0 ||
                System.Text.Encoding.ASCII.GetString(bytes, line2 + 1, line3 - line2 - 1) != "255")
                throw new InvalidDataException("Screenshot PPM must be P6 RGB8");
            string[] size = System.Text.Encoding.ASCII.GetString(bytes, line1 + 1, line2 - line1 - 1).Split(' ');
            width = int.Parse(size[0]); height = int.Parse(size[1]); offset = line3 + 1;
        }
        int channels = format == ImageFileFormat.Tga ? 4 : 3;
        if (width <= 0 || height <= 0 || bytes.Length != checked(offset + width * height * channels))
            throw new InvalidDataException("Screenshot raster has invalid dimensions or truncated pixels");
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = offset + (y * width + x) * channels;
            bitmap.SetPixel(x, y, channels == 4
                ? new SKColor(bytes[i + 2], bytes[i + 1], bytes[i], bytes[i + 3])
                : new SKColor(bytes[i], bytes[i + 1], bytes[i + 2], 255));
        }
        return bitmap;
    }
}
