using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SkiaSharp;
using THSSS.Imaging;

if (args.Length != 0 && args[0] == "fontconfig-child")
{
    FontconfigChecks.RunChild(args);
    return;
}
if (args.Length != 0 && args[0] == "missing-native-child")
{
    NativeLibrary.SetDllImportResolver(typeof(SKBitmap).Assembly, (_, _, _) =>
        throw new DllNotFoundException("deliberately unavailable Skia native dependency"));
    try
    {
        using var stream = new MemoryStream(RasterFixtures.Png());
        using var bitmap = RasterCodec.Decode(stream);
        throw new Exception("Missing native decoder returned an image");
    }
    catch (Exception error) when (MissingNative(error))
    {
        Console.WriteLine("PASS missing-native: image decoding reports unavailable native dependency");
        return;
    }
}

int assertions = 0;
void Check(bool condition, string message)
{
    assertions++;
    if (!condition) throw new Exception(message);
}

// PNG is constructed from literal scanlines by zlib/CRC, independently of Skia encoders.
byte[] pngFixture = RasterFixtures.Png();
var expected = new[] { SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.White };
foreach (var order in new[] { SKColorType.Rgba8888, SKColorType.Bgra8888 })
{
    using var input = new MemoryStream(pngFixture);
    using var decoded = RasterCodec.Decode(input, order);
    Check(decoded.Width == 2 && decoded.Height == 2 && decoded.ColorType == order,
        "Independent PNG dimensions and requested pixel order");
    for (int i = 0; i < expected.Length; i++)
        Check(decoded.GetPixel(i % 2, i / 2) == expected[i], $"Independent PNG RGB and orientation pixel {i}/{order}");
    var bytes = new byte[decoded.ByteCount];
    Marshal.Copy(decoded.GetPixels(), bytes, 0, bytes.Length);
    Check(bytes[0] == (order == SKColorType.Rgba8888 ? 255 : 0) &&
        bytes[2] == (order == SKColorType.Rgba8888 ? 0 : 255) && bytes[3] == 255,
        "RGBA/BGRA physical memory order, not just GetPixel conversion");
}

using (var source = new SKBitmap(8, 6, SKColorType.Rgba8888, SKAlphaType.Unpremul))
{
    source.Erase(new SKColor(213, 39, 81, 128));
    source.SetPixel(0, 0, new SKColor(17, 101, 233, 0));
    source.SetPixel(7, 5, new SKColor(23, 211, 67, 255));
    using var stream = new MemoryStream();
    RasterCodec.Encode(source, stream, RasterFormat.Png);
    stream.Position = 0;
    using var decoded = RasterCodec.Decode(stream, SKColorType.Bgra8888);
    Check(decoded.Width == 8 && decoded.Height == 6, "PNG dimensions");
    Check(decoded.GetPixel(4, 3) == new SKColor(213, 39, 81, 128), "PNG straight-alpha exact non-gray RGBA");
    Check(decoded.GetPixel(0, 0).Alpha == 0 && decoded.GetPixel(7, 5) == new SKColor(23, 211, 67, 255),
        "PNG transparent and opaque corners preserve orientation");
    var raw = new byte[decoded.ByteCount];
    Marshal.Copy(decoded.GetPixels(), raw, 0, raw.Length);
    int offset = (3 * decoded.RowBytes) + 4 * 4;
    Check(raw[offset] == 81 && raw[offset + 1] == 39 && raw[offset + 2] == 213 && raw[offset + 3] == 128,
        "PNG decoded BGRA bytes are straight, not premultiplied");
}

using (var source = new SKBitmap(32, 32, SKColorType.Rgba8888, SKAlphaType.Unpremul))
{
    source.Erase(new SKColor(201, 47, 93, 255));
    using var encoded = new MemoryStream();
    RasterCodec.Encode(source, encoded, RasterFormat.Jpeg);
    byte[] bytes = encoded.ToArray();
    Check(bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255, "JPEG container magic");
    using var stream = new MemoryStream(bytes);
    using var decoded = RasterCodec.Decode(stream, SKColorType.Rgba8888);
    var pixel = decoded.GetPixel(16, 16);
    Check(decoded.Width == 32 && decoded.Height == 32 && pixel.Alpha == 255 &&
        Math.Abs(pixel.Red - 201) <= 3 && Math.Abs(pixel.Green - 47) <= 3 && Math.Abs(pixel.Blue - 93) <= 3,
        $"JPEG independent numeric color expectation (lossy tolerance): {pixel}");
}

using (var bitmap = new Bitmap(64, 48))
using (var graphics = Graphics.FromImage(bitmap))
{
    graphics.Clear(Color.FromArgb(255, 13, 29, 53));
    graphics.FillRectangle(Brushes.Red, 4, 4, 12, 12);
    using var limeBrush = new SolidBrush(Color.Lime);
    graphics.FillEllipse(limeBrush, 24, 4, 12, 12);
    using var pen = new Pen(Color.Blue, 2);
    graphics.DrawRectangle(pen, 4, 25, 12, 12);
    graphics.DrawEllipse(pen, 24, 25, 12, 12);
    graphics.DrawLine(pen, 42, 5, 58, 5);
    var data = bitmap.LockBits(new Rectangle(0, 0, 64, 48), ImageLockMode.ReadOnly, bitmap.PixelFormat);
    var bytes = new byte[64 * 48 * 4];
    try { Marshal.Copy(data.Scan0, bytes, 0, bytes.Length); }
    finally { bitmap.UnlockBits(data); }
    SKColor Pixel(int x, int y)
    {
        int offset = (y * 64 + x) * 4;
        return new SKColor(bytes[offset + 2], bytes[offset + 1], bytes[offset], bytes[offset + 3]);
    }
    Check(Pixel(1, 1) == new SKColor(13, 29, 53), "Shape operations retain prior background");
    Check(Pixel(8, 8) == SKColors.Red, "Filled rectangle survives later operations");
    Check(Pixel(30, 10) == SKColors.Lime && Pixel(24, 4) == new SKColor(13, 29, 53), "Filled ellipse geometry, not rectangle");
    Check(Pixel(4, 30).Blue > 240 && Pixel(10, 30) == new SKColor(13, 29, 53), "Outline rectangle retains hollow interior");
    Check(Pixel(24, 31).Blue > 200 && Pixel(30, 31) == new SKColor(13, 29, 53), "Outline ellipse retains hollow interior");
    Check(Pixel(50, 5).Blue > 240 && Pixel(50, 8) == new SKColor(13, 29, 53), "Line geometry and width");
}

assertions += FontconfigChecks.Run();
assertions += ShapingChecks.Run();
assertions += RasterFixtures.CheckLegacyEncoders();
assertions += RasterBoundaryChecks.Run();
Console.WriteLine($"PASS SkiaCompatibility: {assertions} assertions");

static bool MissingNative(Exception error)
{
    for (Exception current = error; current != null; current = current.InnerException)
        if (current is DllNotFoundException) return true;
    return false;
}
