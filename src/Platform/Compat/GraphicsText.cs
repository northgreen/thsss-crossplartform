using System;
using SkiaSharp;
using THSSS.Imaging;

namespace System.Drawing
{
    public class Graphics : IDisposable
    {
        private readonly Bitmap _target;
        private readonly SKMatrix _identity = SKMatrix.Identity;
        private SKMatrix _transform = SKMatrix.Identity;
        private bool _disposed;
        private Graphics(Bitmap target) { _target = target; }
        public static Graphics FromImage(Image img) => new Graphics(img as Bitmap
            ?? throw new ArgumentException("Graphics requires a Bitmap target.", nameof(img)));
        public Text.TextRenderingHint TextRenderingHint { get; set; }
        internal static SKColor ToSkia(Color c) => new((byte)c.R, (byte)c.G, (byte)c.B, (byte)c.A);
        private static SKColor BrushColor(Brush brush) => ToSkia((brush as SolidBrush)?.Color ?? Color.White);

        private void Paint(Action<SKCanvas> draw)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var source = RasterCodec.FromPixels(_target.Pixels, _target.Width, _target.Height, SKColorType.Bgra8888);
            using var surface = SKSurface.Create(new SKImageInfo(_target.Width, _target.Height, SKColorType.Bgra8888, SKAlphaType.Premul))
                ?? throw new InvalidOperationException("Unable to allocate the Graphics raster surface.");
            var canvas = surface.Canvas;
            canvas.DrawBitmap(source, 0, 0, SKSamplingOptions.Default);
            canvas.SetMatrix(_transform);
            draw(canvas);
            using var image = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);
            var pixels = RasterCodec.CopyPixels(bitmap, SKColorType.Bgra8888);
            Buffer.BlockCopy(pixels, 0, _target.Pixels, 0, pixels.Length);
        }

        public void Clear(Color color) => Paint(canvas => canvas.Clear(ToSkia(color)));
        public void DrawString(string text, Font font, Brush brush, float x, float y)
            => DrawString(text, font, brush, x, y, null);
        public void DrawString(string text, Font font, Brush brush, float x, float y, StringFormat format)
        {
            if (string.IsNullOrEmpty(text)) return;
            using var layout = new SkiaTextLayout(text, font);
            Paint(canvas => layout.Draw(canvas, BrushColor(brush), x, y, 0, format?.Alignment ?? StringAlignment.Near));
        }
        public void DrawString(string text, Font font, Brush brush, RectangleF rect)
        {
            if (string.IsNullOrEmpty(text) || rect.Width <= 0 || rect.Height <= 0) return;
            using var layout = new SkiaTextLayout(text, font, rect.Width);
            Paint(canvas =>
            {
                canvas.ClipRect(SKRect.Create(rect.X, rect.Y, rect.Width, rect.Height));
                layout.Draw(canvas, BrushColor(brush), rect.X, rect.Y, rect.Width);
            });
        }
        private void Stroke(Pen pen, Action<SKCanvas, SKPaint> draw)
        {
            if (pen == null) return;
            using var paint = new SKPaint { Color = ToSkia(pen.Color), Style = SKPaintStyle.Stroke,
                StrokeWidth = pen.Width, IsAntialias = true };
            Paint(canvas => draw(canvas, paint));
        }
        private void Fill(Brush brush, Action<SKCanvas, SKPaint> draw)
        {
            using var paint = new SKPaint { Color = BrushColor(brush), IsAntialias = true };
            Paint(canvas => draw(canvas, paint));
        }
        public void DrawLine(Pen pen, float x1, float y1, float x2, float y2)
            => Stroke(pen, (c, p) => c.DrawLine(x1, y1, x2, y2, p));
        public void DrawRectangle(Pen pen, float x, float y, float w, float h)
            => Stroke(pen, (c, p) => c.DrawRect(SKRect.Create(x, y, w, h), p));
        public void DrawRectangle(Pen pen, Rectangle rect) => DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
        public void FillRectangle(Brush brush, float x, float y, float w, float h)
            => Fill(brush, (c, p) => c.DrawRect(SKRect.Create(x, y, w, h), p));
        public void FillRectangle(Brush brush, Rectangle rect) => FillRectangle(brush, rect.X, rect.Y, rect.Width, rect.Height);
        public void DrawEllipse(Pen pen, float x, float y, float w, float h)
            => Stroke(pen, (c, p) => c.DrawOval(SKRect.Create(x, y, w, h), p));
        public void FillEllipse(Brush brush, float x, float y, float w, float h)
            => Fill(brush, (c, p) => c.DrawOval(SKRect.Create(x, y, w, h), p));
        public void DrawImage(Image image, float x, float y) => DrawImage(image, x, y, image.Width, image.Height);
        public void DrawImage(Image image, Rectangle dest, Rectangle src, GraphicsUnit unit)
            => DrawImageCore(image, SKRect.Create(dest.X, dest.Y, dest.Width, dest.Height),
                SKRect.Create(src.X, src.Y, src.Width, src.Height));
        public void DrawImage(Image image, Rectangle dest) => DrawImage(image, dest.X, dest.Y, dest.Width, dest.Height);
        public void DrawImage(Image image, float x, float y, float w, float h)
            => DrawImageCore(image, SKRect.Create(x, y, w, h), SKRect.Create(image.Width, image.Height));
        private void DrawImageCore(Image image, SKRect dest, SKRect src)
        {
            if (image is not Bitmap source || source.Width <= 0 || source.Height <= 0 || dest.IsEmpty || src.IsEmpty) return;
            // Copy the source before drawing, including self-blits.
            using var bitmap = RasterCodec.FromPixels(source.Pixels, source.Width, source.Height, SKColorType.Bgra8888);
            Paint(canvas => canvas.DrawBitmap(bitmap, src, dest, new SKSamplingOptions(SKFilterMode.Linear)));
        }
        public void TranslateTransform(float dx, float dy) => _transform = SKMatrix.Concat(_transform, SKMatrix.CreateTranslation(dx, dy));
        public void ScaleTransform(float sx, float sy) => _transform = SKMatrix.Concat(_transform, SKMatrix.CreateScale(sx, sy));
        public void RotateTransform(float angle) => _transform = SKMatrix.Concat(_transform, SKMatrix.CreateRotationDegrees(angle));
        public void ResetTransform() => _transform = _identity;
        public void Dispose() { _disposed = true; }
    }
}
