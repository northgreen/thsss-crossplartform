// Cross-platform Texture (replaces SlimDX.Direct3D9.Texture).
// Talks only to the render backend so an OpenGL, Vulkan or future backend can be
// swapped at startup.
using System;
using System.Drawing;
using System.IO;
using Silk.NET.OpenGL;
using SkiaSharp;
using THSSS.Imaging;
using THSSS.Graphics;

namespace SlimDX.Direct3D9;

/// <summary>Cross-platform texture backed by <see cref="IRenderTexture"/>.</summary>
public unsafe class Texture : IDisposable
{
    private readonly IRenderBackend _backend;
    private IRenderTexture _texture;
    private int _width;
    private int _height;
    private int _levels;
    private bool _disposed;

    /// <summary>Backend-neutral GPU handle; the GL/Vulkan name never leaks out.</summary>
    public IRenderHandle Handle => _texture?.Handle;

    /// <summary>Raw GL texture name, for tests that still drive GL directly.</summary>
    public uint GLName => _texture is THSSS.Graphics.OpenGl.OpenGlTexture gl ? gl.Id : 0;
    public bool Disposed => _disposed;
    public int Width => _width;
    public int Height => _height;
    public int LevelCount => _levels;
    public Format Format { get; private set; }
    public Pool Pool { get; private set; }
    public Usage Usage { get; private set; }

    internal IRenderTexture RenderTexture => _texture;

    public Texture(Device device, int width, int height, int levels, Usage usage, Format format, Pool pool)
        : this(device?.RenderBackend ?? throw new ArgumentNullException(nameof(device)),
               width, height, levels, usage, format, pool) { }

    internal Texture(IRenderBackend backend, int width, int height, int levels, Usage usage, Format format, Pool pool)
    {
        _backend = backend;
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        Format = format;
        Pool = pool;
        Usage = usage;
        _levels = Math.Max(1, levels);

        var textureFormat = format == Format.X8R8G8B8 && backend.Features.SupportsRgb8Textures
            ? TextureFormat.Rgb8
            : TextureFormat.Rgba8;
        _texture = (usage & Usage.RenderTarget) != 0
            ? backend.CreateRenderTexture(_width, _height, textureFormat)
            : backend.CreateTexture(_width, _height, textureFormat, ReadOnlySpan<byte>.Empty);
    }

    public static Texture FromFile(Device device, string fileName, Usage usage, Pool pool, Filter filter, Filter mipFilter, int colorKey)
        => FromFile(device, fileName);

    public static Texture FromFile(Device device, string fileName, int width, int height, int mipLevels, Usage usage, Format format, Pool pool)
        => FromFile(device, fileName);

    public SurfaceDescription GetLevelDescription(int level) => new SurfaceDescription();

    public static Texture FromFile(Device device, string fileName)
    {
        fileName = AssetPath.Resolve(fileName);
        using var image = RasterCodec.Decode(fileName);
        return FromImage(device, image);
    }

    public static Texture FromStream(Device device, Stream stream, int width, int height)
    {
        using var image = RasterCodec.Decode(stream);
        return FromImage(device, image);
    }

    public static Texture FromImage(Device device, SKBitmap image)
    {
        // Atlas UVs address source row zero at v=0: upload rows in source order and let
        // the sprite projection handle screen-space Y.
        var pixels = RasterCodec.CopyPixels(image);
        int width = image.Width, height = image.Height;
        var tex = new Texture(device, width, height, 1, Usage.None, Format.A8R8G8B8, Pool.Managed);
        try
        {
            tex._texture.Upload(pixels, width * 4);
            return tex;
        }
        catch
        {
            tex.Dispose();
            throw;
        }
    }

    public static Texture FromFile(Device device, string fileName, int width, int height, int mipLevels, Usage usage, Format format, Pool pool, Filter filter, Filter mipFilter, int colorKey)
        => FromFile(device, fileName);

    public static Texture FromFile(Device device, string fileName, int width, int height, int mipLevels, Usage usage, Format format, Pool pool, Filter filter, Filter mipFilter, int colorKey, ImageInformation? imageInfo)
    {
        if (File.Exists(AssetPath.Resolve(fileName)))
            return FromFile(device, fileName);
        // Placeholder keeps the game running when an optional atlas is missing.
        return new Texture(device, Math.Max(1, width), Math.Max(1, height), 1, usage, format, pool);
    }

    /// <summary>Wraps an existing GPU texture (render targets, text caches).</summary>
    internal static Texture Wrap(Device device, IRenderTexture texture, int width, int height, Usage usage)
        => new Texture(device.RenderBackend, texture, width, height, usage);

    private Texture(IRenderBackend backend, IRenderTexture texture, int width, int height, Usage usage)
    {
        _backend = backend;
        _texture = texture;
        _width = width;
        _height = height;
        _levels = 1;
        Format = Format.A8R8G8B8;
        Pool = Pool.Default;
        Usage = usage;
    }

    public Surface GetSurfaceLevel(int level)
        => new Surface(_backend, _width, _height, (Usage & Usage.RenderTarget) != 0, _texture);

    public DataRectangle LockRectangle(int level, System.Drawing.Rectangle rect, LockFlags flags)
    {
        _lockedRectangle = rect;
        _lockedData = new DataStream(rect.Width * rect.Height * 4, true, true);
        return new DataRectangle { Pitch = rect.Width * 4, Data = _lockedData };
    }

    public DataRectangle LockRectangle(int level, LockFlags flags)
        => LockRectangle(level, new System.Drawing.Rectangle(0, 0, _width, _height), flags);

    private DataStream _lockedData;
    private System.Drawing.Rectangle _lockedRectangle;

    public void UnlockRectangle(int level)
    {
        var bytes = _lockedData.ToArray();
        _texture.Upload(bytes, _lockedRectangle.Width * 4);
        _lockedData = null;
    }

    public void GenerateMipMaps()
    {
        // Mipmaps are generated by the backend when a texture carries levels; the
        // current textures all request a single level, so this is a no-op there.
    }

    public void SetFilter(TextureFilterType minFilter, TextureFilterType magFilter, TextureFilterType mipFilter)
    {
        // D3DTEXF_* (1=Point, 2=Linear, 3=Anisotropic) mapped onto the sampler block.
        const int address = 1; // D3DTADDRESS_WRAP
        _texture.SetSamplerState(new SamplerStateBlock(
            AddressU: address, AddressV: address,
            MinFilter: (int)minFilter, MagFilter: (int)magFilter, MipFilter: (int)mipFilter,
            MaxAnisotropy: 1, MipLodBias: 0f,
            BorderRed: 0f, BorderGreen: 0f, BorderBlue: 0f, BorderAlpha: 0f,
            BorderEnabled: false));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _texture?.Dispose();
        _texture = null;
        _disposed = true;
    }
}

public enum Filter
{
    None = 0,
    Point = 1,
    Linear = 2,
    Triangle = 3,
    Box = 4,
    MirrorDithered = 5,
}

public struct ImageInformation
{
    public int Width;
    public int Height;
    public int Depth;
    public int MipMapCount;
    public Format Format;
    public int Resource_type;
}
