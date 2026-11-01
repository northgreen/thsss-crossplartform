// Surface compatibility layer (render targets).
// Backend-neutral: the framebuffer and colour texture are IRender objects, and the
// D3D9-style accessors (FBO/pixel readback/ToFile) are expressed through the backend.
using System;
using Silk.NET.OpenGL;
using THSSS.Graphics;
using System.Drawing;

namespace SlimDX.Direct3D9;

public unsafe class Surface : IDisposable
{
    private const int _gl_BackBufferAttachment = -1;
    private readonly IRenderBackend _backend;
    private IRenderFramebuffer _framebuffer;
    private IRenderTexture _texture;
    private int _width;
    private int _height;
    private bool _isRenderTarget;
    private bool _disposed;

    // Default framebuffer may be physically larger than the original logical backbuffer.
    internal Rectangle? ReadbackRegion { get; set; }
    internal uint DepthBuffer { get; private set; }
    internal THSSS.Graphics.IRenderDepthSurface? DepthSurface { get; set; }

    public IRenderFramebuffer Framebuffer => _framebuffer;

    /// <summary>
    /// Raw GL framebuffer name, for tests that still drive GL directly. Replace with
    /// backend-level checks when the tests are migrated.
    /// </summary>
    public uint FBO => _framebuffer is THSSS.Graphics.OpenGl.OpenGlFramebuffer glFbo ? glFbo.Fbo : 0;

    /// <summary>Colour texture of a render target, or null for the default backbuffer.</summary>
    public IRenderTexture Texture => _texture;

    public int Width => _width;
    public int Height => _height;
    public bool IsDisposed => _disposed;

    /// <summary>Backbuffer surface from a raw GL context (tests and legacy call sites).</summary>
    public Surface(Silk.NET.OpenGL.GL gl, int width, int height, bool isRenderTarget)
        : this(BackendFor(gl), width, height, isRenderTarget) { }


    private static IRenderBackend BackendFor(Silk.NET.OpenGL.GL gl)
        => new THSSS.Graphics.OpenGl.OpenGlRenderBackend(gl);

    /// <summary>Backbuffer surface (no own colour attachment).</summary>
    public Surface(IRenderBackend backend, int width, int height, bool isRenderTarget)
    {
        _backend = backend;
        _width = width;
        _height = height;
        _isRenderTarget = isRenderTarget;
        if (!isRenderTarget) return;

        _texture = backend.CreateRenderTexture(width, height, TextureFormat.Rgba8);
        // D3D9 render targets are created together with a depth-stencil surface, so a
        // target that later receives DepthStencilSurface already has depth storage.
        _framebuffer = backend.CreateFramebuffer(_texture, DepthFormat.Depth24, width, height);
        if (!_framebuffer.IsComplete)
            throw new InvalidOperationException("Incomplete texture framebuffer.");
    }

    /// <summary>Render target sharing a texture owned by a <see cref="Texture"/>.</summary>
    internal Surface(IRenderBackend backend, int width, int height, bool isRenderTarget, IRenderTexture sharedTexture)
    {
        _backend = backend;
        _width = width;
        _height = height;
        _isRenderTarget = isRenderTarget;
        _texture = sharedTexture;
        if (!isRenderTarget) return;

        _framebuffer = backend.CreateFramebuffer(_texture, DepthFormat.Depth24, width, height);
        if (!_framebuffer.IsComplete)
            throw new InvalidOperationException("Incomplete texture framebuffer.");
    }

    public DataRectangle LockRectangle(LockFlags flags)
        => new() { Pitch = _width * 4, Data = new DataStream(_width * _height * 4, true, true) };

    public DataRectangle LockRectangle(Rectangle rect, LockFlags flags)
        => new() { Pitch = _width * 4, Data = new DataStream(rect.Width * rect.Height * 4, true, true) };

    public void UnlockRectangle() { }

    public static void ToFile(Surface surface, string fileName, ImageFileFormat format)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ObjectDisposedException.ThrowIf(surface.IsDisposed, surface);
        THSSS.Imaging.RasterFormat encoder = format switch
        {
            ImageFileFormat.Jpg => THSSS.Imaging.RasterFormat.Jpeg,
            ImageFileFormat.Png => THSSS.Imaging.RasterFormat.Png,
            ImageFileFormat.Bmp => THSSS.Imaging.RasterFormat.Bmp,
            ImageFileFormat.Tga => THSSS.Imaging.RasterFormat.Tga,
            ImageFileFormat.Ppm => THSSS.Imaging.RasterFormat.Ppm,
            _ => throw new NotSupportedException($"Screenshot encoding {format} is not supported.")
        };
        var backend = surface._backend;
        var region = surface.ReadbackRegion ?? new Rectangle(0, 0, surface.Width, surface.Height);

        // Read from the surface's own framebuffer, or from a temporary one when the
        // surface only wraps a texture; restore whatever was bound before.
        var previousDraw = backend.CurrentDrawFramebuffer;
        var previousRead = backend.CurrentReadFramebuffer;
        if (surface._framebuffer != null)
        {
            backend.BindFramebuffer(surface._framebuffer);
        }
        else if (surface._texture != null)
        {
            using var temporary = backend.CreateFramebuffer(surface._texture, DepthFormat.None,
                surface.Width, surface.Height);
            backend.RestoreFramebufferBindings(temporary, temporary);
        }
        if (surface._framebuffer == null && surface._texture == null)
        {
            // The default framebuffer is not an IRenderFramebuffer object; bind it
            // explicitly so a previously bound render target cannot capture instead.
            backend.RestoreFramebufferBindings(null, null);
            // Default framebuffer: read from the displayed buffer, not an offscreen one.
            backend.SetReadBuffer((int)_gl_BackBufferAttachment);
        }
        else if (surface._framebuffer != null)
        {
            backend.SetReadBuffer(0);
        }
        var pixels = backend.ReadPixels(region.X, region.Y, region.Width, region.Height);
        backend.RestoreFramebufferBindings(previousDraw, previousRead);

        int stride = region.Width * 4;
        var row = new byte[stride];
        // GL row 0 is the bottom for both render targets and the default framebuffer.
        // Every GL source (own FBO, temporary wrap, default framebuffer) is bottom-up.
        int flippedRows = region.Height / 2;
        for (int y = 0; y < flippedRows; y++)
        {
            int top = y * stride, bottom = (region.Height - 1 - y) * stride;
            Array.Copy(pixels, top, row, 0, stride);
            Array.Copy(pixels, bottom, pixels, top, stride);
            Array.Copy(row, 0, pixels, bottom, stride);
        }
        using var image = THSSS.Imaging.RasterCodec.FromPixels(pixels, region.Width, region.Height);
        using var resized = region.Width != surface.Width || region.Height != surface.Height
            ? THSSS.Imaging.RasterCodec.Resize(image, surface.Width, surface.Height) : null;
        using var stream = System.IO.File.Create(fileName);
        THSSS.Imaging.RasterCodec.Encode(resized ?? image, stream, encoder);
    }

    /// <summary>D3D9 depth-stencil surface. Backend render targets own their depth.</summary>
    public static Surface CreateDepthStencil(Device device, int width, int height, Format format,
        MultiSampleType multiSample, int quality, bool discard)
    {
        if (multiSample != MultiSampleType.None)
            throw new NotSupportedException("Multisampled depth requires a matching multisampled render target.");
        var surface = new Surface(device.RenderBackend, width, height, false);
        var depth = device.RenderBackend.CreateDepthSurface(width, height,
            format == Format.D16 ? THSSS.Graphics.DepthFormat.Depth16 : THSSS.Graphics.DepthFormat.Depth24);
        surface.DepthSurface = depth;
        surface.DepthBuffer = depth.NativeAttachmentId;
        return surface;
    }

    public void Bind()
    {
        if (_framebuffer != null) _backend.BindFramebuffer(_framebuffer);
    }

    public void Unbind() => _backend.BindFramebuffer(null);

    public void Dispose()
    {
        if (_disposed) return;
        _framebuffer?.Dispose();
        _framebuffer = null;
        _texture = null;
        DepthBuffer = 0;
        _disposed = true;
    }
}
