using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;

namespace THSSS.Imaging;

public enum RasterFormat { Png, Jpeg, Bmp, Tga, Ppm }

/// <summary>Owned, top-down straight-alpha pixels at the image/GPU boundary.</summary>
public static class RasterCodec
{
    public static SKBitmap Decode(string path, SKColorType colorType = SKColorType.Rgba8888)
    {
        using var stream = File.OpenRead(path);
        return Decode(stream, colorType);
    }

    public static SKBitmap Decode(Stream stream, SKColorType colorType = SKColorType.Rgba8888)
    {
        ArgumentNullException.ThrowIfNull(stream);
        RequireColorType(colorType);
        using var managed = new SKManagedStream(stream, false);
        using var codec = SKCodec.Create(managed) ?? throw new InvalidDataException("Unsupported or invalid image data.");
        _ = PixelBytes(codec.Info.Width, codec.Info.Height);
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, colorType, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        RequireStorage(bitmap);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result == SKCodecResult.Success) return bitmap;
        bitmap.Dispose();
        throw new InvalidDataException($"Image decoding failed: {result}.");
    }

    public static SKBitmap FromPixels(byte[] pixels, int width, int height,
        SKColorType colorType = SKColorType.Rgba8888)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        RequireColorType(colorType);
        int bytes = PixelBytes(width, height);
        if (pixels.Length != bytes) throw new ArgumentException("Pixel buffer length must match the image dimensions.", nameof(pixels));
        var bitmap = new SKBitmap(new SKImageInfo(width, height, colorType, SKAlphaType.Unpremul));
        RequireStorage(bitmap);
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), bytes);
        return bitmap;
    }

    public static byte[] CopyPixels(SKBitmap bitmap, SKColorType colorType = SKColorType.Rgba8888)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        RequireColorType(colorType);
        var bytes = new byte[PixelBytes(bitmap.Width, bitmap.Height)];
        if (bitmap.ColorType == colorType && bitmap.AlphaType == SKAlphaType.Unpremul)
        {
            if (bitmap.GetPixels() == IntPtr.Zero) throw new InvalidDataException("Image pixel storage is unavailable.");
            int stride = checked(bitmap.Width * 4);
            for (int row = 0; row < bitmap.Height; row++)
                Marshal.Copy(IntPtr.Add(bitmap.GetPixels(), checked(row * bitmap.RowBytes)), bytes, row * stride, stride);
            return bytes;
        }
        using var target = new SKBitmap(new SKImageInfo(bitmap.Width, bitmap.Height, colorType, SKAlphaType.Unpremul));
        using var source = bitmap.PeekPixels();
        if (source == null || !source.ReadPixels(target.Info, target.GetPixels(), target.RowBytes))
            throw new InvalidDataException("Cannot convert image pixels to the requested straight-alpha format.");
        Marshal.Copy(target.GetPixels(), bytes, 0, bytes.Length);
        return bytes;
    }

    public static SKBitmap Resize(SKBitmap bitmap, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        _ = PixelBytes(width, height);
        return bitmap.Resize(new SKImageInfo(width, height, bitmap.ColorType, bitmap.AlphaType),
            new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new InvalidDataException("Image resize failed.");
    }

    public static void Encode(SKBitmap bitmap, Stream stream, RasterFormat format)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(stream);
        if (format is RasterFormat.Png or RasterFormat.Jpeg)
        {
            var encoded = format == RasterFormat.Png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
            if (!bitmap.Encode(stream, encoded, format == RasterFormat.Jpeg ? 75 : 100))
                throw new InvalidDataException($"Image encoding failed: {format}.");
            return;
        }
        if (format is not (RasterFormat.Bmp or RasterFormat.Tga or RasterFormat.Ppm))
            throw new NotSupportedException($"Image encoding {format} is not supported.");
        var pixels = CopyPixels(bitmap, SKColorType.Bgra8888);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        switch (format)
        {
            case RasterFormat.Bmp: WriteBmp(writer, bitmap.Width, bitmap.Height, pixels); break;
            case RasterFormat.Tga: WriteTga(writer, bitmap.Width, bitmap.Height, pixels); break;
            case RasterFormat.Ppm: WritePpm(writer, bitmap.Width, bitmap.Height, pixels); break;
        }
    }

    private static void WriteBmp(BinaryWriter writer, int width, int height, byte[] bgra)
    {
        // BITMAPV4HEADER gives 32-bit BGRA an explicit alpha mask; rows remain top-down.
        writer.Write((ushort)0x4d42);
        writer.Write(checked(122 + bgra.Length));
        writer.Write(0);
        writer.Write(122);
        writer.Write(108);
        writer.Write(width);
        writer.Write(-height);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(3);
        writer.Write(bgra.Length);
        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        writer.Write(0x00ff0000u); writer.Write(0x0000ff00u); writer.Write(0x000000ffu); writer.Write(0xff000000u);
        writer.Write(0x73524742u);
        writer.Write(new byte[48]);
        writer.Write(bgra);
    }

    private static void WriteTga(BinaryWriter writer, int width, int height, byte[] bgra)
    {
        if (width > ushort.MaxValue || height > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(width), "TGA dimensions must fit in 16 bits.");
        writer.Write((byte)0); writer.Write((byte)0); writer.Write((byte)2);
        writer.Write(new byte[9]);
        writer.Write((ushort)width); writer.Write((ushort)height);
        writer.Write((byte)32); writer.Write((byte)0x28);
        writer.Write(bgra);
    }

    private static void WritePpm(BinaryWriter writer, int width, int height, byte[] bgra)
    {
        writer.Write(Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
        for (int i = 0; i < bgra.Length; i += 4)
        {
            writer.Write(bgra[i + 2]); writer.Write(bgra[i + 1]); writer.Write(bgra[i]);
        }
    }

    private static int PixelBytes(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive.");
        return checked(width * height * 4);
    }

    private static void RequireColorType(SKColorType colorType)
    {
        if (colorType is not (SKColorType.Rgba8888 or SKColorType.Bgra8888))
            throw new ArgumentOutOfRangeException(nameof(colorType), "Only packed RGBA/BGRA pixels are supported.");
    }

    private static void RequireStorage(SKBitmap bitmap)
    {
        if (bitmap.GetPixels() != IntPtr.Zero) return;
        bitmap.Dispose();
        throw new OutOfMemoryException("The image pixel buffer could not be allocated.");
    }
}
