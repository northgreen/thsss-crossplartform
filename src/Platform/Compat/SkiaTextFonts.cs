using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace System.Drawing
{
    // Native font managers own alias, locale, TTC face and platform fallback policy.
    internal static class SkiaTextFonts
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<(string, FontStyle), SKTypeface> Families = new();
        private static readonly Dictionary<(string, FontStyle, string, int), SKTypeface> Characters = new();
        private static readonly Dictionary<IntPtr, SKShaper> Shapers = new();
        private static readonly Dictionary<IntPtr, SKFont> CoverageFonts = new();

        internal static float PixelSize(Font font) => Math.Max(1, font.Unit switch
        {
            GraphicsUnit.Pixel => font.Size,
            GraphicsUnit.Inch => font.Size * 96,
            GraphicsUnit.Millimeter => font.Size * 96 / 25.4f,
            GraphicsUnit.Document => font.Size * 96 / 300,
            _ => font.Size * 96 / 72
        });

        private static SKFontStyle Style(Font font) => (font.Style & (FontStyle.Bold | FontStyle.Italic)) switch
        {
            FontStyle.Bold => SKFontStyle.Bold,
            FontStyle.Italic => SKFontStyle.Italic,
            FontStyle.Bold | FontStyle.Italic => SKFontStyle.BoldItalic,
            _ => SKFontStyle.Normal
        };

        internal static SKTypeface Primary(Font font)
        {
            lock (Gate)
            {
                var key = (font.Name ?? "", font.Style);
                if (Families.TryGetValue(key, out var face)) return face;
                face = SKFontManager.Default.MatchFamily(font.Name, Style(font))
                    ?? SKFontManager.Default.MatchFamily(null, Style(font))
                    ?? throw new InvalidOperationException("The system font manager found no usable default font.");
                Families.Add(key, face);
                return face;
            }
        }

        internal static SKTypeface ForRune(Font font, Rune rune)
        {
            var primary = Primary(font);
            string language = CultureInfo.CurrentUICulture.Name;
            lock (Gate)
            {
                if (!CoverageFonts.TryGetValue(primary.Handle, out var coverage))
                    CoverageFonts.Add(primary.Handle, coverage = new SKFont(primary));
                if (coverage.ContainsGlyph(rune.Value)) return primary;
                var key = (font.Name ?? "", font.Style, language, rune.Value);
                if (Characters.TryGetValue(key, out var face)) return face;
                face = SKFontManager.Default.MatchCharacter(font.Name, Style(font),
                    string.IsNullOrEmpty(language) ? Array.Empty<string>() : new[] { language }, rune.Value) ?? primary;
                Characters.Add(key, face);
                return face;
            }
        }

        private static bool Covers(SKTypeface face, string element)
        {
            lock (Gate)
            {
                if (!CoverageFonts.TryGetValue(face.Handle, out var coverage))
                    CoverageFonts.Add(face.Handle, coverage = new SKFont(face));
                foreach (Rune rune in element.EnumerateRunes())
                {
                    if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format ||
                        rune.Value is >= 0xFE00 and <= 0xFE0F or >= 0xE0100 and <= 0xE01EF) continue;
                    if (!coverage.ContainsGlyph(rune.Value)) return false;
                }
                return true;
            }
        }

        internal static SKTypeface ForElement(Font font, string element)
        {
            var primary = Primary(font);
            if (Covers(primary, element)) return primary;
            SKTypeface first = null;
            foreach (Rune rune in element.EnumerateRunes())
            {
                var candidate = ForRune(font, rune);
                first ??= candidate;
                if (Covers(candidate, element)) return candidate;
            }
            // An unsupported cluster remains intact rather than breaking its marks/ZWJ sequence.
            return first ?? primary;
        }

        internal static SKShaper.Result Shape(SKTypeface face, string text, SKFont font)
        {
            lock (Gate)
            {
                if (!Shapers.TryGetValue(face.Handle, out var shaper))
                    Shapers.Add(face.Handle, shaper = new SKShaper(face));
                return shaper.Shape(text, font);
            }
        }
        // Faces/shapers deliberately live for the process, not a caller's Font lifetime.
    }
}
