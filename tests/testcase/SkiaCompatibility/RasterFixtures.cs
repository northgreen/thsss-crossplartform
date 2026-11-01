using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using SkiaSharp;
using THSSS.Imaging;

internal static class RasterFixtures
{
    internal static byte[] Png()
    {
        using var file = new MemoryStream();
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        Chunk(file, "IHDR", [0, 0, 0, 2, 0, 0, 0, 2, 8, 6, 0, 0, 0]);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
            zlib.Write(new byte[] { 0, 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 0, 255, 255, 255, 255, 255, 255 });
        Chunk(file, "IDAT", compressed.ToArray());
        Chunk(file, "IEND", []);
        return file.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        byte[] tag = Encoding.ASCII.GetBytes(type);
        output.Write(tag); output.Write(data);
        uint crc = uint.MaxValue;
        foreach (byte value in tag.Concat(data))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        output.Write(number);
    }

    internal static int CheckLegacyEncoders()
    {
        byte[] rgba = [255, 0, 0, 128, 0, 255, 0, 255, 0, 0, 255, 0, 255, 255, 255, 255];
        byte[] bgra = [0, 0, 255, 128, 0, 255, 0, 255, 255, 0, 0, 0, 255, 255, 255, 255];
        using var bitmap = RasterCodec.FromPixels(rgba, 2, 2);
        if (!RasterCodec.CopyPixels(bitmap, SKColorType.Bgra8888).AsSpan().SequenceEqual(bgra))
            throw new Exception("CopyPixels must convert literal straight-alpha RGBA to BGRA without losing transparent RGB");
        foreach (var format in new[] { RasterFormat.Bmp, RasterFormat.Tga, RasterFormat.Ppm })
        {
            using var stream = new MemoryStream();
            RasterCodec.Encode(bitmap, stream, format);
            byte[] bytes = stream.ToArray();
            if (format == RasterFormat.Bmp)
            {
                if (bytes.Length != 138 || bytes[0] != 'B' || bytes[1] != 'M' ||
                    BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10)) != 122 ||
                    BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18)) != 2 ||
                    BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22)) != -2 ||
                    BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(66)) != 0xff000000 ||
                    !bytes.AsSpan(122).SequenceEqual(bgra))
                    throw new Exception("BMP V4 size, top-down dimensions, alpha mask or literal BGRA pixels mismatch");
            }
            else if (format == RasterFormat.Tga)
            {
                byte[] header = [0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 2, 0, 32, 0x28];
                if (!bytes.AsSpan(0, 18).SequenceEqual(header) || !bytes.AsSpan(18).SequenceEqual(bgra))
                    throw new Exception("TGA literal header, top-down alpha descriptor or BGRA pixels mismatch");
            }
            else
            {
                byte[] expected = [80, 54, 10, 50, 32, 50, 10, 50, 53, 53, 10, 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255];
                if (!bytes.AsSpan().SequenceEqual(expected)) throw new Exception("PPM literal P6 header and top-down RGB pixels mismatch");
            }
        }
        Console.WriteLine("PASS literal BMP/TGA/PPM encoded bytes and straight-alpha RGBA/BGRA conversion");
        return 4;
    }
}
