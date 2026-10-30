// Missing types for cross-platform compatibility
// NOTE: Effect, Sprite, Texture, VertexBuffer, Surface, Mesh, Image, Font, Device, SourceVoice,
//       WaveFormat, FilterParameters, FilterType, WaveFormatExtensible, Speakers are defined
//       in their own dedicated files. Do NOT redefine them here.
using System;
using System.Collections.Generic;

namespace SlimDX.Direct3D9
{
    // Data rectangle
    public struct DataRectangle
    {
        public IntPtr DataPointer;
        public int Pitch;
        public DataStream Data;
    }

    // Lock flags
    [Flags]
    public enum LockFlags
    {
        None = 0,
        Discard = 1,
        NoOverwrite = 2,
        ReadOnly = 4,
        NoDirtyUpdate = 8,
    }

    // Index buffer
    public class IndexBuffer : IDisposable
    {
        public IndexBuffer(Device device, int size, Usage usage, Pool pool) { }
        public void Dispose() { }
        public DataStream Lock(int offset, int size, LockFlags flags) => new DataStream(size, true, true);
        public void Unlock() { }
    }

    // Lighting state used by the 3D background/environment helpers.
    public struct Light
    {
        public LightType Type;
        public System.Drawing.Color Diffuse;
        public System.Drawing.Color Ambient;
        public System.Drawing.Color Specular;
        public Vector3 Direction;
        public Vector3 Position;
    }

    /// <summary>D3DCLEAR_* bits.</summary>
    [Flags]
    public enum ClearFlags
    {
        None = 0,
        Target = 1,
        ZBuffer = 2,
        Stencil = 4,
    }

    public class VertexShader : IDisposable
    {
        public void Dispose() { }
    }

    public class PixelShader : IDisposable
    {
        public void Dispose() { }
    }

    // Handle
    public struct Handle
    {
        public IntPtr Value;
    }

    // Sprite flags
    [Flags]
    public enum SpriteFlags
    {
        None = 0,
        AlphaBlend = 1,
        SortTexture = 2,
        SortDepthBackToFront = 4,
        SortDepthFrontToBack = 8,
        DoNotSaveState = 16,
        DoNotModifyRenderState = 32,
    }

    // Font enums
    public enum FontWeight { Normal = 0, Bold = 700 }
    public enum CharacterSet { Default = 1, Ansi = 0 }
    public enum Precision { Default = 0 }
    public enum FontQuality { Default = 0, Antialiased = 4 }
    public enum FontPitch { Default = 0 }

    // Present parameters
    public struct PresentParameters
    {
        public int BackBufferWidth;
        public int BackBufferHeight;
        public Format BackBufferFormat;
        public int BackBufferCount;
        public MultiSampleType MultiSampleType;
        public int MultiSampleQuality;
        public SwapEffect SwapEffect;
        public IntPtr DeviceWindowHandle;
        public bool Windowed;
        public bool EnableAutoDepthStencil;
        public Format AutoDepthStencilFormat;
        public PresentFlags PresentFlags;
        public int FullScreenRefreshRateInHz;
        public PresentInterval PresentationInterval;
        public int PresentIntervalValue;
    }

    // Swap effect
    public enum SwapEffect
    {
        Discard = 1,
        Flip = 2,
        Copy = 3,
    }

    // Multi-sample type
    public enum MultiSampleType
    {
        None = 0,
        NonMaskable = 1,
        TwoSamples = 2,
        ThreeSamples = 3,
        FourSamples = 4,
    }

    // AdapterInformation is in Direct3D9AdditionalTypes.cs

    // Adapter information
    public struct AdapterInformation
    {
        public int Adapter;
        public string Description;
        public string DriverName;
        public Guid DriverGuid;
        public DisplayMode CurrentDisplayMode;
    }

    // Display mode
    public struct DisplayMode
    {
        public int Width;
        public int Height;
        public int RefreshRate;
        public Format Format;
    }

    // Manager
    public static class Manager
    {
        public static int AdapterCount => 1;
        public static AdapterInformation GetAdapterInformation(int adapter) => new AdapterInformation();
        public static DisplayMode[] GetAdapterDisplayMode(int adapter) => new DisplayMode[] { new DisplayMode() };
        public static Capabilities GetDeviceCapabilities(int adapter, DeviceType deviceType) => new Capabilities();
        public static bool CheckDeviceFormat(int adapter, DeviceType deviceType, Format adapterFormat, Usage usage, ResourceType resourceType, Format checkFormat) => true;
        public static bool CheckDeviceType(int adapter, DeviceType deviceType, Format adapterFormat, Format backBufferFormat, bool windowed) => true;
    }

    // Resource type
    public enum ResourceType
    {
        Texture = 0,
        VolumeTexture = 1,
        CubeTexture = 2,
        VertexBuffer = 3,
        IndexBuffer = 4,
    }

    // State block
    public class StateBlock : IDisposable
    {
        public StateBlock(Device device, StateBlockType type) { }
        public void Capture() { }
        public void Apply() { }
        public void Dispose() { }
    }

    public enum StateBlockType
    {
        All = 1,
        PixelState = 2,
        VertexState = 3,
    }

}

namespace SlimDX.Direct3D9
{
    public class Font : IDisposable
    {
        private readonly Device _device;
        private readonly System.Drawing.Font _font;
        private bool _disposed;
        private Texture _cachedGlyphs;
        private (string Text, int Width, int Height, DrawTextFormat Flags, int Color) _glyphKey;
        private float _glyphX, _glyphY;

        public Font(Device device, System.Drawing.Font font)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            ArgumentNullException.ThrowIfNull(font);
            _font = new System.Drawing.Font(font.Name, font.Size, font.Style, font.Unit);
        }

        public Font(Device device, int height, int width, FontWeight weight, int mipLevels,
                    bool italic, CharacterSet charSet, Precision precision,
                    FontQuality quality, FontPitch pitch, string faceName)
            : this(device, new System.Drawing.Font(faceName, Math.Max(1, Math.Abs(height)),
                ((int)weight >= 700 ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular) |
                (italic ? System.Drawing.FontStyle.Italic : System.Drawing.FontStyle.Regular),
                System.Drawing.GraphicsUnit.Pixel)) { }

        private SixLabors.ImageSharp.Drawing.Processing.RichTextOptions Layout(DrawTextFormat flags, int width)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var options = System.Drawing.Graphics.CreateTextOptions(_font);
            if ((flags & DrawTextFormat.WordBreak) != 0 && (flags & DrawTextFormat.SingleLine) == 0 && width > 0)
                options.WrappingLength = width;
            return options;
        }

        private static string FormatText(string text, DrawTextFormat flags)
            => (flags & DrawTextFormat.SingleLine) != 0
                ? text?.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ') : text;

        private System.Drawing.Size Measure(string text, DrawTextFormat flags, int width)
        {
            var options = Layout(flags, width);
            if (string.IsNullOrEmpty(text)) return System.Drawing.Size.Empty;
            var size = SixLabors.Fonts.TextMeasurer.MeasureSize(FormatText(text, flags), options);
            return new System.Drawing.Size((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height));
        }

        public System.Drawing.Size MeasureString(string text) => Measure(text, 0, 0);
        public System.Drawing.Size MeasureString(string text, int width, int height)
            => Measure(text, DrawTextFormat.WordBreak, width);
        public System.Drawing.Size MeasureString(string text, DrawTextFormat flags) => Measure(text, flags, 0);
        public System.Drawing.Size MeasureString(Sprite sprite, string text, DrawTextFormat flags) => Measure(text, flags, 0);
        public System.Drawing.Size MeasureString(Sprite sprite, string text, DrawTextFormat flags, int width)
            => Measure(text, flags, width);

        public void DrawText(Sprite sprite, string text, System.Drawing.Rectangle rect,
                             DrawTextFormat flags, System.Drawing.Color color)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (string.IsNullOrEmpty(text)) return;
            text = FormatText(text, flags);
            if (_cachedGlyphs != null && _glyphKey == (text, rect.Width, rect.Height, flags, color.ToArgb()))
            {
                DrawCached(sprite, rect);
                return;
            }
            var options = Layout(flags, rect.Width);
            var size = Measure(text, flags, rect.Width);
            float x = (flags & DrawTextFormat.Right) != 0 ? rect.Width - size.Width :
                (flags & DrawTextFormat.Center) != 0 ? (rect.Width - size.Width) / 2f : 0;
            float y = (flags & DrawTextFormat.Bottom) != 0 ? rect.Height - size.Height :
                (flags & DrawTextFormat.VCenter) != 0 ? (rect.Height - size.Height) / 2f : 0;
            bool clip = (flags & DrawTextFormat.NoClip) == 0;
            int width = clip ? rect.Width : size.Width;
            int height = clip ? rect.Height : size.Height;
            if (width <= 0 || height <= 0) return;
            options.Origin = new SixLabors.ImageSharp.PointF(clip ? x : 0, clip ? y : 0);
            using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height);
            SixLabors.ImageSharp.Processing.ProcessingExtensions.Mutate(image, ctx =>
                SixLabors.ImageSharp.Drawing.Processing.DrawTextExtensions.DrawText(ctx, options, text,
                    SixLabors.ImageSharp.Color.FromRgba((byte)color.R, (byte)color.G, (byte)color.B, (byte)color.A)));
            _cachedGlyphs?.Dispose();
            _cachedGlyphs = Texture.FromImage(_device, image);
            _glyphKey = (text, rect.Width, rect.Height, flags, color.ToArgb());
            _glyphX = clip ? 0 : x;
            _glyphY = clip ? 0 : y;
            DrawCached(sprite, rect);
        }

        private void DrawCached(Sprite sprite, System.Drawing.Rectangle rect)
        {
            using var ownedSprite = sprite == null ? new Sprite(_device) : null;
            var target = sprite ?? ownedSprite;
            if (ownedSprite != null) target.Begin(SpriteFlags.AlphaBlend);
            try
            {
                target.Draw(_cachedGlyphs, null, null,
                    new SlimDX.Vector3(rect.X + _glyphX, rect.Y + _glyphY, 0),
                    new Color4(1f, 1f, 1f, 1f));
            }
            finally
            {
                if (ownedSprite != null) target.End();
                else target.Flush();
            }
        }

        public void DrawText(Sprite sprite, string text, int x, int y, System.Drawing.Color color)
            => DrawString(sprite, text, x, y, color);
        public void DrawString(Sprite sprite, string text, int x, int y, System.Drawing.Color color)
        {
            var size = MeasureString(text);
            DrawText(sprite, text, new System.Drawing.Rectangle(x, y, size.Width, size.Height), 0, color);
        }
        public void DrawString(Sprite sprite, string text, int x, int y, Color4 color)
            => DrawString(sprite, text, x, y, System.Drawing.Color.FromArgb(
                (int)(Math.Clamp(color.Alpha, 0f, 1f) * 255), (int)(Math.Clamp(color.Red, 0f, 1f) * 255),
                (int)(Math.Clamp(color.Green, 0f, 1f) * 255), (int)(Math.Clamp(color.Blue, 0f, 1f) * 255)));
        public void Dispose() { _cachedGlyphs?.Dispose(); _font.Dispose(); _disposed = true; }
    }

    /// <summary>Vertex element descriptor (SlimDX-compatible).</summary>
    public struct VertexElementDx
    {
    }
}
