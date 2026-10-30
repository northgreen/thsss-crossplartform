// Text and shape rasterisation for System.Drawing.Graphics.
//
// The game never touches a window with Graphics: it draws text into a Bitmap and
// uploads that bitmap as a texture (StageDataPackage.DrawString). So this shim
// renders with ImageSharp.Drawing/SixLabors.Fonts into the managed BGRA buffer
// that Bitmap.LockBits exposes.
using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace System.Drawing
{
    public class Graphics : IDisposable
    {
        private readonly Bitmap _target;

        private Graphics(Bitmap target) { _target = target; }

        public static Graphics FromImage(Image img) => new Graphics(img as Bitmap);

        public Text.TextRenderingHint TextRenderingHint { get; set; }

        private static readonly FontCollection _fonts = new();
        private static readonly object _fontLock = new();
        private static bool _systemFontsLoaded;
        private static readonly Dictionary<(SixLabors.Fonts.FontFamily, SixLabors.Fonts.FontStyle), bool> _readableFaces = new();
        private static readonly Dictionary<(SixLabors.Fonts.FontFamily, SixLabors.Fonts.FontStyle), SixLabors.Fonts.FontFamily[]> _fallbackFamilies = new();

        /// <summary>
        /// Map the game's Windows font names to installed CJK equivalents on
        /// macOS and Linux, without requiring a global font installation.
        /// </summary>
        private static readonly (string Wanted, string[] Equivalents)[] _aliases =
        {
            ("Arial", new[]
            {
                "Liberation Sans", "Noto Sans CJK SC", "Noto Sans CJK JP",
                "DejaVu Sans",
            }),
            ("楷体", new[]
            {
                "Kaiti SC", "STKaiti",
                "Noto Serif CJK SC", "Source Han Serif SC", "Noto Serif CJK JP",
                "WenQuanYi Zen Hei", "Noto Sans CJK SC",
            }),
            ("宋体", new[]
            {
                "Songti SC", "STSong",
                "Noto Serif CJK SC", "Source Han Serif SC", "Noto Serif CJK JP",
                "Noto Sans CJK SC",
            }),
        };

        private static readonly string[] _preferredFamilies =
        {
            "PingFang SC", "Hiragino Sans GB", "Songti SC", "STHeiti", "STSong", "Kaiti SC", "STKaiti",
            "Noto Sans CJK SC", "Noto Sans CJK JP", "Source Han Sans SC",
            "Noto Serif CJK SC", "WenQuanYi Micro Hei", "WenQuanYi Zen Hei",
            "Microsoft YaHei", "SimHei",
            "DejaVu Sans", "Liberation Sans", "Arial",
        };

        private static void EnsureFonts()
        {
            lock (_fontLock)
            {
                if (_systemFontsLoaded) return;
                _fonts.AddSystemFonts();
                _systemFontsLoaded = true;
            }
        }

        /// <summary>
        /// Resolves a FamilyName the way GDI+ did: the requested face when the
        /// machine has it, otherwise any installed CJK-capable fallback so the
        /// Japanese/Chinese dialogue is not rendered as blank boxes.
        /// </summary>
        private static SixLabors.Fonts.Font ResolveFont(Font font, float pixelSize)
        {
            EnsureFonts();
            var size = Math.Max(1f, pixelSize);
            var style = ToStyle(font);

            if (font != null && !string.IsNullOrWhiteSpace(font.Name))
            {
                if (_fonts.TryGet(font.Name, out var exact) && TryCreateReadableFont(exact, size, style, out var requested))
                    return requested;

                foreach (var (wanted, equivalents) in _aliases)
                {
                    if (!string.Equals(wanted, font.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var name in equivalents)
                        if (_fonts.TryGet(name, out var mapped) && TryCreateReadableFont(mapped, size, style, out var replacement))
                            return replacement;
                }
            }

            foreach (var candidate in _preferredFamilies)
                if (_fonts.TryGet(candidate, out var fallback) && TryCreateReadableFont(fallback, size, style, out var preferred))
                    return preferred;

            foreach (var anyFamily in _fonts.Families)
                if (TryCreateReadableFont(anyFamily, size, style, out var available)) return available;

            throw new InvalidOperationException("No system font face readable by SixLabors.Fonts was found; see [FONT] diagnostics.");
        }

        private static bool TryCreateReadableFont(SixLabors.Fonts.FontFamily family, float size,
            SixLabors.Fonts.FontStyle style, out SixLabors.Fonts.Font font)
        {
            lock (_fontLock)
            {
                var key = (family, style);
                if (_readableFaces.TryGetValue(key, out bool readable))
                {
                    font = readable ? family.CreateFont(size, style) : null;
                    return readable;
                }

                var candidate = family.CreateFont(size, style);
                string path = null;
                try
                {
                    candidate.TryGetPath(out path);
                    // Name lookup/CreateFont only reads metadata. UnitsPerEm forces
                    // FileFontMetrics to parse the full face, including required tables.
                    _ = candidate.FontMetrics.UnitsPerEm;
                }
                catch (Exception error) when (error is SixLabors.Fonts.InvalidFontFileException or SixLabors.Fonts.FontException or NotSupportedException)
                {
                    // A system catalog can contain faces this renderer cannot parse.
                    // InvalidFontFileException (including MissingFontTableException)
                    // and FontException are separate exception hierarchies in Fonts 2.x.
                    // Its CFF parser also throws NotSupportedException for unsupported faces.
                    // Isolate that optional candidate, never a failed text draw.
                    _readableFaces.Add(key, false);
                    Console.Error.WriteLine($"[FONT] Excluding unreadable face: family={family.Name} style={style} path={path ?? "(unavailable)"}{Environment.NewLine}{error}");
                    font = null;
                    return false;
                }

                _readableFaces.Add(key, true);
                font = candidate;
                return true;
            }
        }

        private static SixLabors.Fonts.FontFamily[] FallbackFamilies(SixLabors.Fonts.Font primary,
            SixLabors.Fonts.FontStyle style)
        {
            lock (_fontLock)
            {
                var key = (primary.Family, style);
                if (_fallbackFamilies.TryGetValue(key, out var cached)) return cached;
                foreach (string name in _preferredFamilies)
                {
                    if (!_fonts.TryGet(name, out var family) || family == primary.Family) continue;
                    if (!TryCreateReadableFont(family, primary.Size, style, out var fallback)) continue;
                    // TryGetGlyphs can return the .notdef glyph for missing text.
                    // Test cmap coverage instead of accepting that placeholder.
                    bool containsChinese = false;
                    foreach (var codePoint in fallback.FontMetrics.GetAvailableCodePoints())
                        if (codePoint.Value == 0x590F) { containsChinese = true; break; }
                    if (!containsChinese) continue;

                    // SixLabors runs positioning on every fallback, even for ASCII.
                    // Keep one validated CJK fallback, not the whole system catalog.
                    var selected = new[] { family };
                    _fallbackFamilies.Add(key, selected);
                    return selected;
                }
                var empty = Array.Empty<SixLabors.Fonts.FontFamily>();
                _fallbackFamilies.Add(key, empty);
                return empty;
            }
        }

        private static SixLabors.Fonts.FontStyle ToStyle(Font font)
        {
            if (font == null) return SixLabors.Fonts.FontStyle.Regular;
            bool bold = (font.Style & FontStyle.Bold) != 0;
            bool italic = (font.Style & FontStyle.Italic) != 0;
            return (bold, italic) switch
            {
                (true, true) => SixLabors.Fonts.FontStyle.BoldItalic,
                (true, false) => SixLabors.Fonts.FontStyle.Bold,
                (false, true) => SixLabors.Fonts.FontStyle.Italic,
                _ => SixLabors.Fonts.FontStyle.Regular,
            };
        }

        internal static RichTextOptions CreateTextOptions(Font font)
        {
            var primary = ResolveFont(font, PointsToPixels(font));
            return new RichTextOptions(primary)
            {
                WrappingLength = -1f,
                FallbackFontFamilies = FallbackFamilies(primary, ToStyle(font)),
            };
        }

        private static float PointsToPixels(Font font)
        {
            // GDI+ sizes are points at 96 dpi; the game passes point sizes.
            float size = font?.Size ?? 12f;
            return font?.Unit == GraphicsUnit.Pixel ? size : size * 96f / 72f;
        }

        private static SixLabors.ImageSharp.Color ToImageSharpColor(Brush brush)
        {
            var c = (brush as SolidBrush)?.Color ?? Color.White;
            return SixLabors.ImageSharp.Color.FromRgba((byte)c.R, (byte)c.G, (byte)c.B, (byte)c.A);
        }

        public void Clear(Color color)
        {
            if (_target == null) return;
            for (int i = 0; i < _target.Pixels.Length; i += 4)
            {
                _target.Pixels[i] = (byte)color.B;
                _target.Pixels[i + 1] = (byte)color.G;
                _target.Pixels[i + 2] = (byte)color.R;
                _target.Pixels[i + 3] = (byte)color.A;
            }
        }

        public void DrawString(string text, Font font, Brush brush, float x, float y)
            => DrawStringCore(text, font, brush, new PointF(x, y));

        public void DrawString(string text, Font font, Brush brush, float x, float y, StringFormat format)
            => DrawStringCore(text, font, brush, new PointF(x, y), format);

        public void DrawString(string text, Font font, Brush brush, RectangleF rect)
            => DrawStringCore(text, font, brush, new PointF(rect.X, rect.Y));

        private void DrawStringCore(string text, Font font, Brush brush, PointF origin, StringFormat format = null)
        {
            if (_target == null || string.IsNullOrEmpty(text)) return;
            var color = ToImageSharpColor(brush);

            using var layer = new Image<Bgra32>(_target.Width, _target.Height);
            var options = CreateTextOptions(font);
            options.Origin = new SixLabors.ImageSharp.PointF(origin.X, origin.Y);
            if (format != null)
            {
                options.HorizontalAlignment = format.Alignment switch
                {
                    StringAlignment.Center => HorizontalAlignment.Center,
                    StringAlignment.Far => HorizontalAlignment.Right,
                    _ => HorizontalAlignment.Left,
                };
            }
            layer.Mutate(ctx => ctx.DrawText(options, text, color));
            layer.CopyPixelDataTo(_target.Pixels);
        }

        public void DrawLine(Pen pen, float x1, float y1, float x2, float y2)
        {
            if (_target == null || pen == null) return;
            using var layer = new Image<Bgra32>(_target.Width, _target.Height);
            var colour = ToImageSharpColor(new SolidBrush(pen.Color));
            layer.Mutate(ctx => ctx.DrawLine(colour, pen.Width,
                new SixLabors.ImageSharp.PointF(x1, y1), new SixLabors.ImageSharp.PointF(x2, y2)));
            layer.CopyPixelDataTo(_target.Pixels);
        }

        public void DrawRectangle(Pen pen, float x, float y, float w, float h)
        {
            if (_target == null || pen == null) return;
            using var layer = new Image<Bgra32>(_target.Width, _target.Height);
            var colour = ToImageSharpColor(new SolidBrush(pen.Color));
            layer.Mutate(ctx => ctx.Draw(colour, pen.Width,
                new SixLabors.ImageSharp.RectangleF(x, y, w, h)));
            layer.CopyPixelDataTo(_target.Pixels);
        }

        public void DrawRectangle(Pen pen, Rectangle rect)
            => DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);

        public void FillRectangle(Brush brush, float x, float y, float w, float h)
        {
            if (_target == null) return;
            using var layer = new Image<Bgra32>(_target.Width, _target.Height);
            layer.Mutate(ctx => ctx.Fill(ToImageSharpColor(brush),
                new SixLabors.ImageSharp.RectangleF(x, y, w, h)));
            layer.CopyPixelDataTo(_target.Pixels);
        }

        public void FillRectangle(Brush brush, Rectangle rect)
            => FillRectangle(brush, rect.X, rect.Y, rect.Width, rect.Height);

        public void DrawEllipse(Pen pen, float x, float y, float w, float h)
        {
            if (_target == null || pen == null) return;
            using var layer = new Image<Bgra32>(_target.Width, _target.Height);
            var colour = ToImageSharpColor(new SolidBrush(pen.Color));
            layer.Mutate(ctx => ctx.Draw(colour, pen.Width,
                new EllipsePolygon(x + w / 2f, y + h / 2f, w / 2f, h / 2f)));
            layer.CopyPixelDataTo(_target.Pixels);
        }

        public void FillEllipse(Brush brush, float x, float y, float w, float h)
        {
            if (_target == null) return;
            using var layer = new Image<Bgra32>(_target.Width, _target.Height);
            layer.Mutate(ctx => ctx.Fill(ToImageSharpColor(brush),
                new EllipsePolygon(x + w / 2f, y + h / 2f, w / 2f, h / 2f)));
            layer.CopyPixelDataTo(_target.Pixels);
        }

        public void DrawImage(Image image, float x, float y) => DrawImage(image, x, y, image.Width, image.Height);

        public void DrawImage(Image image, Rectangle dest, Rectangle src, GraphicsUnit unit)
            => DrawImage(image, dest.X, dest.Y, dest.Width, dest.Height);

        public void DrawImage(Image image, Rectangle dest)
            => DrawImage(image, dest.X, dest.Y, dest.Width, dest.Height);

        public void DrawImage(Image image, float x, float y, float w, float h)
        {
            var source = image as Bitmap;
            if (_target == null || source?.Pixels == null || source.Width == 0 || source.Height == 0) return;

            using var layer = SixLabors.ImageSharp.Image.LoadPixelData<Bgra32>(
                source.Pixels, source.Width, source.Height);
            layer.Mutate(ctx => ctx.Resize(Math.Max(1, (int)w), Math.Max(1, (int)h)));
            layer.CopyPixelDataTo(_target.Pixels);
        }

        public void TranslateTransform(float dx, float dy) { }
        public void ScaleTransform(float sx, float sy) { }
        public void RotateTransform(float angle) { }
        public void ResetTransform() { }
        public void Dispose() { }
    }
}
