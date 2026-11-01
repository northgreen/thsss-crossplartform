using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SkiaSharp;

namespace System.Drawing
{
    internal sealed class SkiaTextLayout : IDisposable
    {
        private sealed record Run(SKTextBlob Blob, float X);
        private sealed class Line
        {
            internal readonly List<Run> Runs = new();
            internal float Left, Right, Ascent, Descent, Leading, Baseline;
            internal float Width => Right - Left;
        }
        private readonly List<Line> _lines = new();
        internal float Width { get; private set; }
        internal float Height { get; private set; }
        internal Size Size => new((int)Math.Ceiling(Width), (int)Math.Ceiling(Height));

        internal SkiaTextLayout(string text, Font font, float wrapWidth = 0)
        {
            ArgumentNullException.ThrowIfNull(font);
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                foreach (string paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                    AddParagraph(paragraph, font, wrapWidth);
                foreach (var line in _lines)
                {
                    line.Baseline = Height - line.Ascent;
                    Height += line.Descent - line.Ascent + line.Leading;
                    Width = Math.Max(Width, line.Width);
                }
            }
            catch { Dispose(); throw; }
        }

        private void AddParagraph(string text, Font font, float width)
        {
            if (width <= 0 || text.Length == 0) { _lines.Add(ShapeLine(text, font)); return; }
            int start = 0;
            int[] boundaries = StringInfo.ParseCombiningCharacters(text);
            while (start < boundaries.Length)
            {
                int end = start + 1, lastSpace = -1;
                Line accepted = null;
                while (end <= boundaries.Length)
                {
                    int stop = end == boundaries.Length ? text.Length : boundaries[end];
                    var candidate = ShapeLine(text.Substring(boundaries[start], stop - boundaries[start]), font);
                    if (candidate.Width > width && accepted != null) { DisposeLine(candidate); break; }
                    if (accepted != null) DisposeLine(accepted);
                    accepted = candidate;
                    if (char.IsWhiteSpace(text[boundaries[end - 1]])) lastSpace = end;
                    end++;
                }
                int next = end - 1;
                if (end <= boundaries.Length && lastSpace > start && lastSpace < next)
                {
                    DisposeLine(accepted);
                    next = lastSpace;
                    accepted = ShapeLine(text.Substring(boundaries[start], boundaries[next] - boundaries[start]), font);
                }
                _lines.Add(accepted);
                start = next;
            }
        }

        private static Line ShapeLine(string text, Font font)
        {
            var line = new Line();
            using (var primary = new SKFont(SkiaTextFonts.Primary(font), SkiaTextFonts.PixelSize(font)))
            {
                var metrics = primary.Metrics;
                line.Ascent = metrics.Ascent;
                line.Descent = metrics.Descent;
                line.Leading = Math.Max(0, metrics.Leading);
            }
            float advance = 0;
            try
            {
                foreach (var run in FontRuns(text, font)) AddRun(line, run.Text, run.Face, font, ref advance);
                line.Right = Math.Max(line.Right, advance);
                return line;
            }
            catch { DisposeLine(line); throw; }
        }

        private static IEnumerable<(string Text, SKTypeface Face)> FontRuns(string text, Font font)
        {
            var buffer = new StringBuilder();
            SKTypeface current = null;
            var elements = StringInfo.GetTextElementEnumerator(text);
            while (elements.MoveNext())
            {
                string element = elements.GetTextElement();
                var face = SkiaTextFonts.ForElement(font, element);
                if (current != null && face.Handle != current.Handle)
                {
                    yield return (buffer.ToString(), current);
                    buffer.Clear();
                }
                current = face;
                buffer.Append(element);
            }
            if (buffer.Length > 0) yield return (buffer.ToString(), current);
        }

        private static void AddRun(Line line, string text, SKTypeface face, Font drawingFont, ref float advance)
        {
            using var font = new SKFont(face, SkiaTextFonts.PixelSize(drawingFont));
            var shaped = SkiaTextFonts.Shape(face, text, font);
            var glyphs = Array.ConvertAll(shaped.Codepoints, value => checked((ushort)value));
            font.GetGlyphWidths(glyphs, out var bounds);
            for (int i = 0; i < glyphs.Length; i++)
            {
                var b = bounds[i];
                if (b.IsEmpty) continue;
                line.Left = Math.Min(line.Left, advance + shaped.Points[i].X + b.Left);
                line.Right = Math.Max(line.Right, advance + shaped.Points[i].X + b.Right);
                line.Ascent = Math.Min(line.Ascent, shaped.Points[i].Y + b.Top);
                line.Descent = Math.Max(line.Descent, shaped.Points[i].Y + b.Bottom);
            }
            var metrics = font.Metrics;
            line.Ascent = Math.Min(line.Ascent, metrics.Ascent);
            line.Descent = Math.Max(line.Descent, metrics.Descent);
            line.Leading = Math.Max(line.Leading, metrics.Leading);
            using var builder = new SKTextBlobBuilder();
            var run = builder.AllocatePositionedRun(font, glyphs.Length);
            run.SetGlyphs(glyphs);
            run.SetPositions(shaped.Points);
            var blob = builder.Build();
            if (blob != null) line.Runs.Add(new Run(blob, advance));
            advance += shaped.Width;
        }

        internal void Draw(SKCanvas canvas, SKColor color, float x, float y,
            float availableWidth = 0, StringAlignment alignment = StringAlignment.Near)
        {
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            foreach (var line in _lines)
            {
                float extra = availableWidth - line.Width;
                float offset = alignment == StringAlignment.Center ? extra / 2 : alignment == StringAlignment.Far ? extra : 0;
                foreach (var run in line.Runs)
                    canvas.DrawText(run.Blob, x + offset - line.Left + run.X, y + line.Baseline, paint);
            }
        }

        private static void DisposeLine(Line line)
        {
            foreach (var run in line.Runs) run.Blob.Dispose();
        }
        public void Dispose()
        {
            foreach (var line in _lines) DisposeLine(line);
            _lines.Clear();
        }
    }
}
