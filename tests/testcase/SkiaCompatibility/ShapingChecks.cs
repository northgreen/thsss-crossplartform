using System.Drawing;
using System.Reflection;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

internal static class ShapingChecks
{
    private static readonly Type Layout = typeof(Bitmap).Assembly.GetType("System.Drawing.SkiaTextLayout", true)!;
    private static readonly Type Fonts = typeof(Bitmap).Assembly.GetType("System.Drawing.SkiaTextFonts", true)!;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static int Run()
    {
        int assertions = 0;
        void Check(bool value, string message)
        {
            assertions++;
            if (!value) throw new Exception(message);
        }
        using var font = new Font("DejaVu Serif", 32, FontStyle.Regular, GraphicsUnit.Pixel);
        var primary = (SKTypeface)Fonts.GetMethod("Primary", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [font])!;
        using var nativeFont = new SKFont(primary, 32);
        using var shaper = new SKShaper(primary);
        Check(shaper.Shape("fi", nativeFont).Codepoints.Length == 1, "Installed serif must actually shape the fi ligature");
        using var fi = Create("fi", font);
        Check(Math.Abs(Width(fi) - shaper.Shape("fi", nativeFont).Width) < 1, "Layout preserves ligature advance, not separate f+i advances");
        using var composed = Create("é", font);
        using var combining = Create("e\u0301", font);
        Check(Math.Abs(Width(composed) - Width(combining)) < 0.01f, "Combining acute and composed é have equal shaped advance");
        Check(Render(composed).SequenceEqual(Render(combining)), "Combining and composed acute produce identical glyph pixels");
        foreach (string cluster in new[] { "e\u0301", "👩\u200d👩\u200d👧\u200d👦" })
        {
            using var single = Create(cluster, font, 1);
            using var pair = Create(cluster + "X", font, 1);
            Check(Lines(single) == 1 && Lines(pair) == 2, "Narrow wrapping must not split Unicode cluster " + cluster);
            var runs = (IEnumerable)Layout.GetMethod("FontRuns", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [cluster, font])!;
            string[] texts = runs.Cast<object>().Select(run => (string)((ITuple)run)[0]!).ToArray();
            Check(texts.Length == 1 && texts[0] == cluster, "Fallback must not split marks/ZWJ across faces");
        }
        using var italic = new Font("DejaVu Serif", 32, FontStyle.Italic, GraphicsUnit.Pixel);
        var italicFace = (SKTypeface)Fonts.GetMethod("Primary", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [italic])!;
        using var italicNative = new SKFont(italicFace, 32);
        italicNative.GetGlyphWidths(italicNative.GetGlyphs("j"), out var bearing);
        Check(bearing[0].Left < 0, "Negative-bearing fixture must really extend left of the origin");
        foreach (var sample in new[] { ("j", italic), ("fi", font), ("e\u0301", font), ("A夏B", font), ("👩\u200d👩\u200d👧\u200d👦", font) })
        {
            using var layout = Create(sample.Item1, sample.Item2);
            var pixels = Render(layout);
            int left = 256, top = 128, right = -1, bottom = -1, count = 0;
            for (int y = 0; y < 128; y++)
            for (int x = 0; x < 256; x++)
            {
                if (pixels[(y * 256 + x) * 4 + 3] == 0) continue;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y); count++;
            }
            float height = (float)Layout.GetProperty("Height", Hidden)!.GetValue(layout)!;
            Check(count > 10 && left >= 7 && top >= 7 && right <= 8 + Math.Ceiling(Width(layout)) + 1 && bottom <= 8 + Math.Ceiling(height) + 1,
                $"Shaped measurement must enclose visible ink {sample.Item1}: ({left},{top})-({right},{bottom}), {Width(layout)}x{height}");
            var elementFace = (SKTypeface)Fonts.GetMethod("ForElement", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [font, "夏"])!;
            using var coverage = new SKFont(elementFace);
            Check(coverage.ContainsGlyph(0x590f), "Mixed-script fallback resolves an actual 夏 glyph");
        }
        Console.WriteLine($"PASS shaping: {assertions} assertions (ligature, combining mark, fallback, negative bearing, ZWJ clusters)");
        return assertions;
    }

    private static IDisposable Create(string text, Font font, float width = 0) =>
        (IDisposable)Activator.CreateInstance(Layout, Hidden, null, [text, font, width], null)!;
    private static float Width(IDisposable layout) => (float)Layout.GetProperty("Width", Hidden)!.GetValue(layout)!;
    private static int Lines(IDisposable layout) => ((ICollection)Layout.GetField("_lines", Hidden)!.GetValue(layout)!).Count;
    private static byte[] Render(IDisposable layout)
    {
        using var bitmap = new SKBitmap(256, 128, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(SKColors.Transparent);
        using var canvas = new SKCanvas(bitmap);
        Layout.GetMethod("Draw", Hidden)!.Invoke(layout, [canvas, SKColors.White, 8f, 8f, 0f, StringAlignment.Near]);
        return THSSS.Imaging.RasterCodec.CopyPixels(bitmap, SKColorType.Rgba8888);
    }
}
