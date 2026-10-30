// Direct3D9 device emulated with OpenGL for the cross-platform build.
using System;
using Silk.NET.OpenGL;
using SlimDX.DirectInput;

namespace SlimDX.Direct3D9;

public partial class Device : IDisposable
{
    private BlendingFactor _sourceBlend = BlendingFactor.SrcAlpha;
    private BlendingFactor _destinationBlend = BlendingFactor.OneMinusSrcAlpha;
    private Device3D _pipeline3D;

    private Device3D Pipeline3D => _pipeline3D ??= new Device3D(this, GL);

    public Device() { }

    public Device(GL gl, int width, int height)
    {
        GL = gl;
        CurrentContext = gl;
        Width = width;
        Height = height;
        Viewport = new Viewport(0, 0, width, height, 0f, 1f);
    }

    /// <summary>
    /// Adopts the GL context owned by the window layer. The game reaches this
    /// constructor through Direct3D.CreateDevice after Program set CurrentContext.
    /// </summary>
    public Device(Direct3D direct3D, int adapter, DeviceType deviceType, IntPtr handle,
                  CreateFlags createFlags, PresentParameters parameters)
    {
        // Only the GL backend needs the window context; other backends own their device.
        GL = CurrentContext;
        if (THSSS.Graphics.RenderBackendSelection.Requested == THSSS.Graphics.BackendKind.OpenGl && GL == null)
            throw new InvalidOperationException(
                "Direct3D9.Device requires the OpenGL context published by the window layer.");
        Width = parameters.BackBufferWidth > 0 ? parameters.BackBufferWidth : 640;
        Height = parameters.BackBufferHeight > 0 ? parameters.BackBufferHeight : 480;
        GameWidth = Width;
        GameHeight = Height;
        Viewport = new Viewport(0, 0, Width, Height, 0f, 1f);
    }

    public static GL CurrentContext { get; set; }

    private THSSS.Graphics.IRenderTexture _whiteFallback;
    /// <summary>Opaque one-pixel texture used when a stage has nothing bound (D3D9 default).</summary>
    internal THSSS.Graphics.IRenderTexture WhiteFallbackTexture
        => _whiteFallback ??= CreateWhiteFallback();

    private THSSS.Graphics.IRenderTexture CreateWhiteFallback()
    {
        var tex = RenderBackend.CreateTexture(1, 1, THSSS.Graphics.TextureFormat.Rgba8,
            new byte[] { 255, 255, 255, 255 });
        return tex;
    }

    /// <summary>
    /// The GL context is still what the window layer hands over; the backend wraps it
    /// so the rest of the compatibility layer only sees IRenderBackend.
    /// </summary>
    public GL GL { get; }

    private THSSS.Graphics.IRenderBackend _renderBackend;
    public THSSS.Graphics.IRenderBackend RenderBackend => _renderBackend ??= CreateBackend();

    private THSSS.Graphics.IRenderBackend CreateBackend()
        => THSSS.Graphics.RenderBackendSelection.Requested switch
        {
            THSSS.Graphics.BackendKind.OpenGl => new THSSS.Graphics.OpenGl.OpenGlRenderBackend(GL),
            THSSS.Graphics.BackendKind.Vulkan => new THSSS.Graphics.Vulkan.VulkanRenderBackend(
                THSSS.Graphics.RenderBackendSelection.SurfaceSource),
            THSSS.Graphics.BackendKind.Metal => THSSS.Graphics.MetalFactory.Create(THSSS.Graphics.RenderBackendSelection.CocoaWindow),
            _ => throw new PlatformNotSupportedException(
                "Unsupported renderer: " + THSSS.Graphics.RenderBackendSelection.Requested),
        };

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Logical resolution the game was authored for.</summary>
    public int GameWidth { get; set; } = 640;

    public int GameHeight { get; set; } = 480;

    /// <summary>Physical window framebuffer size, published by the window layer.</summary>
    public int FramebufferWidth { get; set; }

    public int FramebufferHeight { get; set; }

    public bool ResizeFramebuffer(int width, int height)
    {
        if (width <= 0 || height <= 0) return false;
        if (FramebufferWidth == width && FramebufferHeight == height) return true;
        RenderBackend.Resize(width, height);
        FramebufferWidth = width;
        FramebufferHeight = height;
        return true;
    }

    private Viewport _viewport;
    public Viewport Viewport
    {
        get => _viewport;
        set { FlushActiveSpriteBatch(); _viewport = value; }
    }

    internal Effect ActiveEffect { get; set; }
    internal Sprite ActiveSpriteBatch { get; set; }
    internal void FlushActiveSpriteBatch() => ActiveSpriteBatch?.Flush();
    public Surface RenderTarget { get; private set; }

    public Surface GetRenderTarget(int index) => RenderTarget;

    private Surface _depthStencilSurface;
    public Surface DepthStencilSurface
    {
        get => _depthStencilSurface;
        set { FlushActiveSpriteBatch(); _depthStencilSurface = value; AttachDepthSurface(); }
    }

    private void AttachDepthSurface()
    {
        // Without an explicit D3D9 depth surface the target keeps whatever depth it was
        // created with; overwriting the attachment with 0 would disable depth testing.
        if (_depthStencilSurface == null) return;
        int width = RenderTarget?.Width ?? (FramebufferWidth > 0 ? FramebufferWidth : GameWidth);
        int height = RenderTarget?.Height ?? (FramebufferHeight > 0 ? FramebufferHeight : GameHeight);
        bool matches = _depthStencilSurface.Width == width && _depthStencilSurface.Height == height;
        RenderBackend.AttachDepth(matches ? _depthStencilSurface.DepthSurface : null);
    }

    // Fixed-function shader state forwarded into the OpenGL 3D pipeline.
    private VertexDeclaration _vertexDeclaration;
    public VertexDeclaration VertexDeclaration
    {
        get => _vertexDeclaration;
        set { _vertexDeclaration = value; Pipeline3D.SetVertexDeclaration(value); }
    }

    private VertexFormat _vertexFormat;
    public VertexFormat VertexFormat
    {
        get => _vertexFormat;
        set { VertexDeclaration = null; _vertexFormat = value; }
    }

    private Material _material;
    public Material Material
    {
        get => _material;
        set { _material = value; Pipeline3D.SetMaterial(value); }
    }

    public VertexShader VertexShader { get; set; }

    public PixelShader PixelShader { get; set; }

    public Direct3D Direct3D { get; } = new();

    public bool IsDisposed { get; private set; }

    public Capabilities Capabilities => new();

    // ---- render state -----------------------------------------------------
    private bool _alphaBlendEnabled;
    private bool _depthWriteEnabled = true;
    private BlendOperation _blendOperation = BlendOperation.Add;
    internal bool DepthWriteEnabled => _depthWriteEnabled;

    private static THSSS.Graphics.BlendFactor ToContract(BlendingFactor factor) => factor switch
    {
        BlendingFactor.Zero => THSSS.Graphics.BlendFactor.Zero,
        BlendingFactor.One => THSSS.Graphics.BlendFactor.One,
        BlendingFactor.SrcColor => THSSS.Graphics.BlendFactor.SourceColor,
        BlendingFactor.OneMinusSrcColor => THSSS.Graphics.BlendFactor.InverseSourceColor,
        BlendingFactor.SrcAlpha => THSSS.Graphics.BlendFactor.SourceAlpha,
        BlendingFactor.OneMinusSrcAlpha => THSSS.Graphics.BlendFactor.InverseSourceAlpha,
        BlendingFactor.DstColor => THSSS.Graphics.BlendFactor.DestinationColor,
        BlendingFactor.OneMinusDstColor => THSSS.Graphics.BlendFactor.InverseDestinationColor,
        BlendingFactor.DstAlpha => THSSS.Graphics.BlendFactor.DestinationAlpha,
        BlendingFactor.OneMinusDstAlpha => THSSS.Graphics.BlendFactor.InverseDestinationAlpha,
        _ => THSSS.Graphics.BlendFactor.SourceAlphaSaturate,
    };
    public void SetRenderState(RenderState state, int value)
    {
        FlushActiveSpriteBatch();
        Pipeline3D.SetState(state, value);
        if (state is RenderState.FogStart or RenderState.FogEnd or RenderState.FogDensity)
            Pipeline3D.SetState(state, BitConverter.Int32BitsToSingle(value));
        switch (state)
        {
            case RenderState.AlphaBlendEnable:
                _alphaBlendEnabled = value != 0;
                ApplyBlendState();
                break;
            case RenderState.ZWriteEnable:
                _depthWriteEnabled = value != 0;
                RenderBackend.SetDepthWrite(_depthWriteEnabled);
                break;
            case RenderState.BlendOperation:
                _blendOperation = (BlendOperation)value;
                ApplyBlendState();
                break;
            case RenderState.MultisampleAntialias:
                SetCapability(THSSS.Graphics.BackendCapability.Multisample, value != 0);
                break;
            case RenderState.ZEnable:
                SetCapability(THSSS.Graphics.BackendCapability.DepthTest, value != 0);
                Pipeline3D.SetDepthTest(value != 0);
                break;
            case RenderState.ScissorTestEnable:
                SetCapability(THSSS.Graphics.BackendCapability.ScissorTest, value != 0);
                break;
            case RenderState.SrcBlend: // same slot as SourceBlend (19)
                _sourceBlend = ToBlendingFactor(value);
                ApplyBlendState();
                break;
            case RenderState.DestBlend: // same slot as DestinationBlend (20)
                _destinationBlend = ToBlendingFactor(value);
                ApplyBlendState();
                break;
        }
    }

    public void SetRenderState(RenderState state, bool value) => SetRenderState(state, value ? 1 : 0);
    public void SetRenderState(RenderState state, float value)
    {
        if (state is RenderState.FogStart or RenderState.FogEnd or RenderState.FogDensity)
            Pipeline3D.SetState(state, value);
        else SetRenderState(state, (int)value);
    }
    public void SetRenderState(RenderState state, Blend value) => SetRenderState(state, (int)value);
    public void SetRenderState(RenderState state, BlendOperation value) => SetRenderState(state, (int)value);
    public void SetRenderState(RenderState state, Compare value) { }
    public void SetRenderState(RenderState state, Cull value) => SetRenderState(state,(int)value);
    public void SetRenderState(RenderState state, FillMode value) { }
    public void SetRenderState(RenderState state, ShadeMode value) { }
    public void SetRenderState(RenderState state, FogMode value) => SetRenderState(state, (int)value);
    public void SetRenderState(RenderState state, TransformType value) { }

    public int GetRenderState(RenderState state) => 0;

    private void SetCapability(THSSS.Graphics.BackendCapability capability, bool enabled)
        => RenderBackend.SetCapability(capability, enabled);

    /// <summary>D3DBLEND_* constants to GL factors. 1 is ZERO and 2 is ONE.</summary>
    private static BlendingFactor ToBlendingFactor(int value) => value switch
    {
        1 => BlendingFactor.Zero,
        2 => BlendingFactor.One,
        3 => BlendingFactor.SrcColor,
        4 => BlendingFactor.OneMinusSrcColor,
        5 => BlendingFactor.SrcAlpha,
        6 => BlendingFactor.OneMinusSrcAlpha,
        7 => BlendingFactor.DstAlpha,
        8 => BlendingFactor.OneMinusDstAlpha,
        9 => BlendingFactor.DstColor,
        10 => BlendingFactor.OneMinusDstColor,
        _ => BlendingFactor.One,
    };

    public void SetSamplerState(int sampler, SamplerState state, int value) => StoreSamplerState(sampler,state,value);
    public void SetSamplerState(int sampler, SamplerState state, TextureFilter value) => StoreSamplerState(sampler,state,(int)value);
    public void SetSamplerState(int sampler, SamplerState state, TextureAddress value) => StoreSamplerState(sampler,state,(int)value);
    public void SetTextureStageState(int stage, TextureStage state, int value) { }
    public void SetTextureStageState(int stage, TextureStage state, TextureOperation value) { }
    public void SetTextureStageState(int stage, TextureStage state, TextureArgument value) { }
    public void SetTextureStageState(int stage, TextureStage state, TextureFilterType value) { }
    public void SetTexture(int stage, Texture texture)
    {
        if (stage == 0) Pipeline3D.SetTexture(texture);
    }
    public void SetTransform(TransformType type, Matrix matrix)
        => SetTransform(type switch
        {
            TransformType.World => TransformState.World,
            TransformType.View => TransformState.View,
            TransformType.Projection => TransformState.Projection,
            _ => throw new NotSupportedException("Unsupported fixed-function transform: " + type),
        },matrix);

    public void SetTransform(TransformState type, Matrix matrix)
    {
        if (type is TransformState.World or TransformState.View or TransformState.Projection)
            Pipeline3D.SetTransform(type, matrix);
    }

    /// <summary>Read back the game's world/view/projection without synthesizing defaults.</summary>
    public Matrix GetTransform(TransformType type)
    {
        var m = type switch
        {
            TransformType.World => Pipeline3D.World,
            TransformType.View => Pipeline3D.View,
            TransformType.Projection => Pipeline3D.Projection,
            _ => throw new NotSupportedException("Unsupported fixed-function transform: " + type),
        };
        return new Matrix
        {
            M11 = m.M11, M12 = m.M12, M13 = m.M13, M14 = m.M14,
            M21 = m.M21, M22 = m.M22, M23 = m.M23, M24 = m.M24,
            M31 = m.M31, M32 = m.M32, M33 = m.M33, M34 = m.M34,
            M41 = m.M41, M42 = m.M42, M43 = m.M43, M44 = m.M44,
        };
    }
    public void SetMaterial(ref Material material) => Material = material;
    public void SetLight(int index, ref Light light) => Pipeline3D.SetLight(index, light);
    public void SetLight(int index, Light light) => Pipeline3D.SetLight(index, light);
    public void EnableLight(int index, bool enable) => Pipeline3D.EnableLight(index, enable);
    public void SetViewport(Viewport viewport) => Viewport = viewport;
    public Viewport GetViewport() => Viewport;

    public void BeginScene() { }

    public void EndScene() => FlushActiveSpriteBatch();

    /// <summary>GLFW presents the default framebuffer after the render callback.</summary>
    public void Present()
    {
        FlushActiveSpriteBatch();
        if (PresentSourceProvider is { } provide)
            RenderBackend.SetPresentSource(provide());
        (RenderBackend as THSSS.Graphics.Vulkan.VulkanRenderBackend)?.NotePresentState();
        RenderBackend.Present();
        if (GL != null) FramebufferPresent?.Invoke();
    }

    /// <summary>Window swap for the GL backend, installed by the window layer.</summary>
    public static Action? FramebufferPresent { get; set; }

    /// <summary>
    /// Supplies the texture holding the finished frame, which presenting backends blit to
    /// the window. GL ignores it; the window swaps its own back buffer.
    /// </summary>
    public Func<THSSS.Graphics.IRenderTexture?>? PresentSourceProvider { get; set; }

    /// <summary>The game presents through this overload; both forms reach the backend.</summary>
    public void Present(Present flags) => Present();

    // ---- immediate geometry ----------------------------------------------
    // User geometry and the shipped models share the same validated 3D stream path.
    public void DrawUserPrimitives<T>(PrimitiveType type, int primitiveCount, T[] data) where T : struct
        => DrawUserPrimitives(type,primitiveCount,data,VertexFormat);
    public void DrawUserPrimitives<T>(PrimitiveType type, int primitiveCount, T[] data, VertexFormat format) where T : struct
    {
        ArgumentNullException.ThrowIfNull(data);
        if (System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            throw new ArgumentException("Vertex structs cannot contain managed references.",nameof(data));
        int size = System.Runtime.InteropServices.Marshal.SizeOf<T>();
        using var buffer = new VertexBuffer(this,size*data.Length,Usage.WriteOnly,format,Pool.Managed);
        var stream = buffer.Lock(0,0,LockFlags.Discard);
        var pointer = System.Runtime.InteropServices.Marshal.AllocHGlobal(size*data.Length);
        try
        {
            for (int i=0;i<data.Length;i++) System.Runtime.InteropServices.Marshal.StructureToPtr(data[i],pointer+i*size,false);
            stream.WriteRange(pointer,size*data.Length);
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(pointer); }
        buffer.Unlock();
        SetFVF(format);
        SetStreamSource(0,buffer,0,size);
        DrawPrimitives(type,0,primitiveCount);
    }
    public void DrawPrimitives(PrimitiveType type, int startVertex, int primitiveCount)
    {
        FlushActiveSpriteBatch();
        ApplyBlendState();
        Pipeline3D.Draw(type, startVertex, primitiveCount);
    }
    public void DrawIndexedPrimitives(PrimitiveType type, int baseVertex, int startVertex, int vertexCount, int startIndex, int primitiveCount)
        => throw new NotSupportedException("Indexed device drawing is not used by the shipped game; Mesh.DrawSubset expands validated indices into a vertex stream.");
    public void SetStreamSource(int streamNumber, VertexBuffer streamData, int offsetInBytes, int stride)
    {
        _streamOffset = offsetInBytes;
        Pipeline3D.SetStreamSource(streamData, offsetInBytes, stride);
    }

    private int _streamOffset;

    /// <summary>Blend factor currently requested through SetRenderState.</summary>
    internal BlendingFactor CurrentSourceBlend => _sourceBlend;
    internal BlendingFactor CurrentDestinationBlend => _destinationBlend;

    /// <summary>Applies the blend mode the game configured for the next draw.</summary>
    internal void ApplyBlendState()
    {
        RenderBackend.SetBlendState(new THSSS.Graphics.BlendState(
            _alphaBlendEnabled,
            ToContract(_sourceBlend),
            ToContract(_destinationBlend),
            _blendOperation switch
            {
                BlendOperation.Subtract => THSSS.Graphics.BlendEquation.Subtract,
                BlendOperation.RevSubtract => THSSS.Graphics.BlendEquation.ReverseSubtract,
                BlendOperation.Min => THSSS.Graphics.BlendEquation.Min,
                BlendOperation.Max => THSSS.Graphics.BlendEquation.Max,
                _ => THSSS.Graphics.BlendEquation.Add,
            }));
    }

    internal (BlendingFactor Source, BlendingFactor Destination, BlendOperation Operation, bool Enabled) CaptureBlendState()
        => (_sourceBlend, _destinationBlend, _blendOperation, _alphaBlendEnabled);
    internal void RestoreBlendState((BlendingFactor Source, BlendingFactor Destination, BlendOperation Operation, bool Enabled) state)
    {
        _sourceBlend = state.Source;
        _destinationBlend = state.Destination;
        _blendOperation = state.Operation;
        _alphaBlendEnabled = state.Enabled;
        ApplyBlendState();
    }

    /// <summary>Resets blend to the sprite batcher's default (SrcAlpha/1-SrcAlpha).</summary>
    internal void ResetBlendState()
    {
        _sourceBlend = BlendingFactor.SrcAlpha;
        _destinationBlend = BlendingFactor.OneMinusSrcAlpha;
        GL.BlendFunc(_sourceBlend, _destinationBlend);
    }
    public void SetIndices(IndexBuffer indexData) => Pipeline3D.SetIndices(indexData);
    public void SetVertexDeclaration(VertexDeclaration declaration)
    {
        VertexDeclaration = declaration;
        Pipeline3D.SetVertexDeclaration(declaration);
    }
    public void SetVertexShader(VertexShader shader) { }
    public void SetPixelShader(PixelShader shader) { }
    public void SetFVF(VertexFormat format) { VertexDeclaration = null; VertexFormat = format; }

    public Surface GetBackBuffer(int swapChain, int index)
    {
        int width = FramebufferWidth > 0 ? FramebufferWidth : GameWidth;
        int height = FramebufferHeight > 0 ? FramebufferHeight : GameHeight;
        var content = THSSS.Graphics.PresentationGeometry.Fit(width, height, GameWidth, GameHeight);
        return new Surface(RenderBackend,GameWidth,GameHeight,false)
        {
            ReadbackRegion = new System.Drawing.Rectangle(content.X, content.Y, content.Width, content.Height)
        };
    }
    public Surface GetDepthStencilSurface() => DepthStencilSurface;

    public void SetRenderTarget(int index, Surface surface)
    {
        FlushActiveSpriteBatch();
        RenderTarget = surface;
        RenderBackend.BindFramebuffer(surface?.Framebuffer);
        Viewport = new Viewport(0, 0, surface?.Width ?? GameWidth, surface?.Height ?? GameHeight, 0, 1);
        AttachDepthSurface();
    }

    public void SetDepthStencilSurface(Surface surface) => DepthStencilSurface = surface;

    public void Clear(ClearFlags flags, System.Drawing.Color color, float z, int stencil)
    {
        FlushActiveSpriteBatch();
        if (GL == null)
        {
            RenderBackend.Clear(BackendClearMask(flags), color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f, z, stencil);
            return;
        }
        GL.Disable(Silk.NET.OpenGL.EnableCap.ScissorTest);
        int width = RenderTarget?.Width ?? Math.Max(1, FramebufferWidth > 0 ? FramebufferWidth : Viewport.Width);
        int height = RenderTarget?.Height ?? Math.Max(1, FramebufferHeight > 0 ? FramebufferHeight : Viewport.Height);
        GL.Viewport(0, 0, (uint)width, (uint)height);
        GL.ClearColor(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        GL.ClearDepth(z);
        GL.ClearStencil(stencil);
        if ((flags & ClearFlags.ZBuffer) != 0) GL.DepthMask(true);
        GL.Clear(ToClearMask(flags));
        GL.DepthMask(_depthWriteEnabled);
    }

    public void Clear(ClearFlags flags, int color, float z, int stencil)
        => Clear(flags, System.Drawing.Color.FromArgb((color >> 16) & 0xFF, (color >> 8) & 0xFF, color & 0xFF), z, stencil);

    public void Clear(ClearFlags flags, Color4 color, float z, int stencil)
    {
        FlushActiveSpriteBatch();
        if (GL == null)
        {
            RenderBackend.Clear(BackendClearMask(flags), color.Red, color.Green, color.Blue, color.Alpha, z, stencil);
            return;
        }
        GL.Disable(Silk.NET.OpenGL.EnableCap.ScissorTest);
        GL.ClearColor(color.Red, color.Green, color.Blue, color.Alpha);
        GL.ClearDepth(z);
        GL.ClearStencil(stencil);
        if ((flags & ClearFlags.ZBuffer) != 0) GL.DepthMask(true);
        GL.Clear(ToClearMask(flags));
        GL.DepthMask(_depthWriteEnabled);
    }

    private static THSSS.Graphics.ClearMask BackendClearMask(ClearFlags flags)
        => ((flags & ClearFlags.Target) != 0 ? THSSS.Graphics.ClearMask.Color : 0)
         | ((flags & ClearFlags.ZBuffer) != 0 ? THSSS.Graphics.ClearMask.Depth : 0)
         | ((flags & ClearFlags.Stencil) != 0 ? THSSS.Graphics.ClearMask.Stencil : 0);

    private static ClearBufferMask ToClearMask(ClearFlags flags)
    {
        var mask = ClearBufferMask.None;
        if ((flags & ClearFlags.Target) != 0) mask |= ClearBufferMask.ColorBufferBit;
        if ((flags & ClearFlags.ZBuffer) != 0) mask |= ClearBufferMask.DepthBufferBit;
        if ((flags & ClearFlags.Stencil) != 0) mask |= ClearBufferMask.StencilBufferBit;
        return mask;
    }

    public Surface CreateOffscreenPlainSurface(int width, int height, Format format, Pool pool)
        => new(RenderBackend, width, height, false);

    public Surface CreateRenderTarget(int width, int height, Format format, MultiSampleType multiSample, int quality, bool lockable)
        => new(RenderBackend, width, height, true);

    public Texture CreateTexture(int width, int height, int levels, Usage usage, Format format, Pool pool)
        => new(this, width, height, levels, usage, format, pool);

    public VertexBuffer CreateVertexBuffer(int size, Usage usage, VertexFormat format, Pool pool)
        => new(this, size, usage, format, pool);

    public IndexBuffer CreateIndexBuffer(int size, Usage usage, Format format, Pool pool)
        => new(this, size, usage, pool);

    public VertexDeclaration CreateVertexDeclaration(VertexElement[] elements) => new(this, elements);

    public void DrawText(Sprite sprite, string text, System.Drawing.Rectangle rect, DrawTextFormat flags, System.Drawing.Color color) { }
    public void DrawText(Font font, string text, System.Drawing.Rectangle rect, DrawTextFormat flags, System.Drawing.Color color) { }

    public StateBlock CreateStateBlock(StateBlockType type) => new(this, type);

    /// <summary>The emulated device never loses its context.</summary>
    public Result TestCooperativeLevel() => Result.Ok;

    public void Reset(PresentParameters parameters) { }

    public void Dispose()
    {
        if (IsDisposed) return;
        _pipeline3D?.Dispose();
        if (_sampler0 != 0) { GL.DeleteSampler(_sampler0); _sampler0 = 0; }
        IsDisposed = true;
    }
}
