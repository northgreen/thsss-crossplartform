// Additional types for cross-platform compatibility
using System;
using System.Collections.Generic;
using System.IO;

namespace SlimDX
{
    // NOTE: Vector2/Vector3/Vector4 live in MathTypes.cs (single source of truth).

    // Quaternion
    public struct Quaternion
    {
        public float X, Y, Z, W;
        public Quaternion(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
        public static Quaternion Identity => new Quaternion(0, 0, 0, 1);
        public static Quaternion RotationYawPitchRoll(float yaw, float pitch, float roll)
        {
            float cy = (float)Math.Cos(yaw * 0.5f), sy = (float)Math.Sin(yaw * 0.5f);
            float cp = (float)Math.Cos(pitch * 0.5f), sp = (float)Math.Sin(pitch * 0.5f);
            float cr = (float)Math.Cos(roll * 0.5f), sr = (float)Math.Sin(roll * 0.5f);
            return new Quaternion(
                sr * cp * cy - cr * sp * sy,
                cr * sp * cy + sr * cp * sy,
                cr * cp * sy - sr * sp * cy,
                cr * cp * cy + sr * sp * sy);
        }
    }

    // Rectangle is in System.Drawing (AdditionalTypes.cs) and used via using directives
    // SlimDX.Rectangle removed to avoid ambiguity with System.Drawing.Rectangle

    // Viewport
    public struct Viewport
    {
        public int X, Y, Width, Height;
        public float MinZ, MaxZ;
        public Viewport(int x, int y, int w, int h, float minZ, float maxZ) { X = x; Y = y; Width = w; Height = h; MinZ = minZ; MaxZ = maxZ; }
    }

    // DataStream
    //
    // The original game locks a VertexBuffer, writes vertices into the stream and
    // unlocks. That only works if the stream actually accumulates bytes, so this
    // keeps a real buffer and a real position pointer.
    public class DataStream : IDisposable
    {
        private byte[] _data;
        private int _position;

        public DataStream(int size, bool canRead, bool canWrite)
            => _data = new byte[Math.Max(0, size)];

        public DataStream(byte[] data, bool canRead, bool canWrite)
            => _data = data ?? Array.Empty<byte>();

        public DataStream(IntPtr data, int size, bool canRead, bool canWrite)
            => _data = new byte[Math.Max(0, size)];

        /// <summary>Bytes written so far (or the whole buffer for caller-owned arrays).</summary>
        public byte[] ToArray() => _data;

        public void Dispose() { }

        public void Write<T>(T value) where T : unmanaged
        {
            int size = System.Runtime.InteropServices.Marshal.SizeOf<T>();
            Ensure(size);
            System.Runtime.InteropServices.MemoryMarshal.Write(_data.AsSpan(_position, size), in value);
            _position += size;
        }

        public void WriteRange<T>(T[] values) where T : unmanaged
        {
            if (values == null || values.Length == 0) return;
            int size = values.Length * System.Runtime.InteropServices.Marshal.SizeOf<T>();
            Ensure(size);
            var span = _data.AsSpan(_position, size);
            for (int i = 0; i < values.Length; i++)
                System.Runtime.InteropServices.MemoryMarshal.Write(
                    span.Slice(i * System.Runtime.InteropServices.Marshal.SizeOf<T>()), in values[i]);
            _position += size;
        }

        public void WriteRange(IntPtr source, int size)
        {
            Ensure(size);
            System.Runtime.InteropServices.Marshal.Copy(source, _data, _position, size);
            _position += size;
        }

        public void ReadRange<T>(T[] values) where T : unmanaged
        {
            if (values == null || values.Length == 0) return;
            int size = values.Length * System.Runtime.InteropServices.Marshal.SizeOf<T>();
            if (_position + size > _data.Length) return;
            var span = _data.AsSpan(_position, size);
            for (int i = 0; i < values.Length; i++)
            {
                int offset = i * System.Runtime.InteropServices.Marshal.SizeOf<T>();
                values[i] = System.Runtime.InteropServices.MemoryMarshal.Read<T>(span.Slice(offset));
            }
            _position += size;
        }

        public T Read<T>() where T : unmanaged
        {
            int size = System.Runtime.InteropServices.Marshal.SizeOf<T>();
            if (_position + size > _data.Length) return default;
            var value = System.Runtime.InteropServices.MemoryMarshal.Read<T>(_data.AsSpan(_position, size));
            _position += size;
            return value;
        }

        public byte[] ReadBytes(int count)
        {
            count = Math.Clamp(count, 0, Math.Max(0, _data.Length - _position));
            var result = _data.AsSpan(_position, count).ToArray();
            _position += count;
            return result;
        }

        public long Position
        {
            get => _position;
            set => _position = (int)Math.Clamp(value, 0, _data.Length);
        }

        public long Length => _data?.Length ?? 0;
        public IntPtr DataPointer => IntPtr.Zero;

        private void Ensure(int size)
        {
            if (_position + size <= _data.Length) return;
            Array.Resize(ref _data, _position + size);
        }
    }

    // Result
    public struct Result
    {
        public int Code;
        public Result(int code) { Code = code; }
        public static Result Ok = new Result(0);
        public static Result Fail = new Result(-1);
        public static Result Last = new Result(0);
        public bool IsSuccess => Code >= 0;
        public bool IsFailure => Code < 0;
        public string Description => Code == 0 ? "Success" : "Failure";
        public static bool operator ==(Result a, Result b) => a.Code == b.Code;
        public static bool operator !=(Result a, Result b) => a.Code != b.Code;
        public override bool Equals(object obj) => obj is Result r && r.Code == Code;
        public override int GetHashCode() => Code;
    }
}

// System.Drawing compatibility
namespace System.Drawing
{
    // Color is defined in SlimDX.Direct3D9 namespace to avoid ambiguity
    // Game code uses SlimDX.Direct3D9.Color via using directives


    public class Font : IDisposable
    {
        public string Name { get; set; }
        public float Size { get; set; }
        public FontStyle Style { get; set; }
        public GraphicsUnit Unit { get; set; }
        // GDI+ points are the default for the constructors the game uses.
        public Font(string name, float size) { Name = name; Size = size; Unit = GraphicsUnit.Point; }
        public Font(string name, float size, FontStyle style) { Name = name; Size = size; Style = style; Unit = GraphicsUnit.Point; }
        public Font(string name, float size, GraphicsUnit unit) { Name = name; Size = size; Unit = unit; }
        public Font(string name, float size, FontStyle style, GraphicsUnit unit)
        { Name = name; Size = size; Style = style; Unit = unit; }
        public void Dispose() { }
    }

    [Flags]
    public enum FontStyle
    {
        Regular = 0, Bold = 1, Italic = 2, Underline = 4, Strikeout = 8,
    }

    public enum GraphicsUnit { Pixel = 0, Point = 1, Inch = 2, Document = 3, Millimeter = 4, World = 5, Display = 6 }

    public class Brush : IDisposable { public void Dispose() { } }
    public static class Brushes
    {
        public static Brush Black => new SolidBrush(Color.Black);
        public static Brush White => new SolidBrush(Color.White);
        public static Brush Red => new SolidBrush(Color.Red);
        public static Brush Green => new SolidBrush(Color.Green);
        public static Brush Blue => new SolidBrush(Color.Blue);
        public static Brush Yellow => new SolidBrush(Color.Yellow);
        public static Brush Gray => new SolidBrush(Color.Gray);
        public static Brush Orange => new SolidBrush(Color.Orange);
    }

    public class SolidBrush : Brush
    {
        public Color Color { get; set; }
        public SolidBrush(Color color) { Color = color; }
    }
    public class TextureBrush : Brush
    {
        public TextureBrush(Image image) { }
        public TextureBrush(Image image, Rectangle rect) { }
    }

    public class Pen : IDisposable
    {
        public Color Color { get; set; }
        public float Width { get; set; }
        public Pen(Color color) { Color = color; Width = 1; }
        public Pen(Color color, float width) { Color = color; Width = width; }
        public Pen(Brush brush) { Width = 1; }
        public Pen(Brush brush, float width) { Width = width; }
        public void Dispose() { }
    }

    public class Image : IDisposable
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public Imaging.PixelFormat PixelFormat { get; set; }

        /// <summary>
        /// BGRA, top-down row order, matching what System.Drawing exposed through
        /// LockBits. Text rasterised by Graphics ends up here and is uploaded to a
        /// texture, so this buffer is what the game actually displays.
        /// </summary>
        internal byte[] Pixels;

        public static Image FromFile(string filename) => new Bitmap(filename);
        public void Dispose() { }
    }
    public class Bitmap : Image
    {
        public Bitmap() { Width = 0; Height = 0; Pixels = Array.Empty<byte>(); }
        public Bitmap(int width, int height) : this(width, height, Imaging.PixelFormat.Format32bppArgb) { }
        public Bitmap(int width, int height, System.Drawing.Imaging.PixelFormat format)
        {
            Width = width; Height = height; PixelFormat = format;
            Pixels = new byte[Math.Max(0, width) * Math.Max(0, height) * 4];
        }

        public Bitmap(string filename)
        {
            using var image = THSSS.Imaging.RasterCodec.Decode(filename, SkiaSharp.SKColorType.Bgra8888);
            Load(image);
        }

        public Bitmap(Stream stream)
        {
            using var image = THSSS.Imaging.RasterCodec.Decode(stream, SkiaSharp.SKColorType.Bgra8888);
            Load(image);
        }

        private void Load(SkiaSharp.SKBitmap image)
        {
            Width = image.Width;
            Height = image.Height;
            PixelFormat = Imaging.PixelFormat.Format32bppArgb;
            Pixels = THSSS.Imaging.RasterCodec.CopyPixels(image, SkiaSharp.SKColorType.Bgra8888);
        }

        public void SetResolution(float xDpi, float yDpi) { }

        /// <summary>Pins the managed pixel buffer so callers can read it via Scan0.</summary>
        public BitmapData LockBits(Rectangle rect, Imaging.ImageLockMode mode, Imaging.PixelFormat format)
        {
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(Pixels,
                System.Runtime.InteropServices.GCHandleType.Pinned);
            _locks.Add(handle);
            return new BitmapData
            {
                Scan0 = handle.AddrOfPinnedObject(),
                Stride = Width * 4,
                Width = Width,
                Height = Height,
                PixelFormat = format,
            };
        }

        private readonly List<System.Runtime.InteropServices.GCHandle> _locks = new();

        public void UnlockBits(BitmapData data)
        {
            if (_locks.Count == 0) return;
            var handle = _locks[_locks.Count - 1];
            _locks.RemoveAt(_locks.Count - 1);
            handle.Free();
        }

        public Color GetPixel(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return Color.Empty;
            int i = (y * Width + x) * 4;
            return Color.FromArgb(Pixels[i + 3], Pixels[i + 2], Pixels[i + 1], Pixels[i]);
        }

        public void SetPixel(int x, int y, Color color)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            int i = (y * Width + x) * 4;
            Pixels[i] = (byte)color.B;
            Pixels[i + 1] = (byte)color.G;
            Pixels[i + 2] = (byte)color.R;
            Pixels[i + 3] = (byte)color.A;
        }
    }

    // Graphics lives in GraphicsText.cs (Skia text and shape rasterisation).

    public class StringFormat : IDisposable
    {
        public StringAlignment Alignment { get; set; }
        public StringAlignment LineAlignment { get; set; }
        public StringFormat(StringAlignment alignment) { Alignment = alignment; }
        public StringFormat(StringAlignment alignment, StringAlignment lineAlignment) { Alignment = alignment; LineAlignment = lineAlignment; }
        public void Dispose() { }
    }

    public enum StringAlignment { Near = 0, Center = 1, Far = 2 }

    public static class SystemColors
    {
        public static Color Control => Color.FromArgb(240, 240, 240);
        public static Color Window => Color.FromArgb(255, 255, 255);
        public static Color WindowText => Color.FromArgb(0, 0, 0);
        public static Color ControlText => Color.FromArgb(0, 0, 0);
        public static Color Highlight => Color.FromArgb(0, 120, 215);
    }
}

namespace System.Drawing.Imaging
{
    public enum PixelFormat
    {
        Format1bppIndexed = 0,
        Format4bppIndexed = 1,
        Format8bppIndexed = 2,
        Format16bppRgb555 = 3,
        Format16bppRgb565 = 4,
        Format16bppArgb1555 = 5,
        Format24bppRgb = 6,
        Format32bppRgb = 7,
        Format32bppArgb = 8,
        Format32bppPArgb = 9,
    }
}

namespace System.Drawing.Text
{
    public enum TextRenderingHint
    {
        SystemDefault = 0,
        SingleBitPerPixelGridFit = 1,
        SingleBitPerPixel = 2,
        AntiAliasGridFit = 3,
        AntiAlias = 4,
        ClearTypeGridFit = 5,
    }
}

namespace System.Drawing.Imaging
{
    public enum ImageLockMode
    {
        ReadOnly = 1,
        WriteOnly = 2,
        ReadWrite = 3,
        UserInputBuffer = 4,
    }
}
