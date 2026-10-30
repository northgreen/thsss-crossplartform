#nullable enable
using System;
using System.Collections.Generic;
using static THSSS.Graphics.Metal.MetalNative;

namespace THSSS.Graphics.Metal;

public sealed unsafe partial class MetalRenderBackend : IRenderBackend
{
    internal nint Device { get; }
    private readonly nint _queue, _layer, _pool;
    private nint _framePool;
    private readonly List<MetalResource> _resources=new();
    private readonly Dictionary<SamplerStateBlock,nint> _samplers=new();
    private readonly Dictionary<(bool,bool),nint> _depthStates=new();
    private MetalFramebuffer? _draw,_read,_backbuffer;
    private MetalTexture? _texture,_presentSource,_fallback;
    private readonly Dictionary<int,byte[]> _uniforms=new();
    private bool _disposed,_frameOpen,_depthTest,_depthWrite=true,_scissorEnabled;
    private CullMode _cull;
    private BlendState _blend=new(false,BlendFactor.One,BlendFactor.Zero,BlendEquation.Add);
    private SamplerStateBlock? _sampling;
    private int _width=640,_height=480;
    private Viewport _viewport=new(0,0,640,480,0,1);
    private (int X,int Y,int Width,int Height) _scissor;
    public BackendKind Kind=>BackendKind.Metal;
    // RGB8 uses RGBA storage with opaque alpha, including render-target writes.
    public BackendFeatures Features=>new(true,true,true,1,"msl");
    public int PresentedFrames{get;private set;}
    public long TotalSubmissions{get;private set;}
    public byte[]? LastPresentedPixels{get;private set;}
    public IRenderFramebuffer? CurrentDrawFramebuffer=>_draw;
    public IRenderFramebuffer? CurrentReadFramebuffer=>_read;
    public MetalRenderBackend(nint cocoaWindow=0)
    {
        if(!OperatingSystem.IsMacOS()&&Environment.GetEnvironmentVariable("THSSS_METAL_API_SHIM")!="1")throw new PlatformNotSupportedException("Native Metal requires macOS; API-shim tests must explicitly set THSSS_METAL_API_SHIM=1.");
        // Loading Metal first registers its Foundation dependencies before Objective-C class lookup.
        Device=Require(MTLCreateSystemDefaultDevice(),"MTLCreateSystemDefaultDevice (no Metal device available)");
        try
        {
            _pool=New("NSAutoreleasePool");
            _queue=Require(Call(Device,"newCommandQueue"),"newCommandQueue");
            if(cocoaWindow==0)return;
            if(OperatingSystem.IsMacOS())System.Runtime.InteropServices.NativeLibrary.Load("/System/Library/Frameworks/QuartzCore.framework/QuartzCore");
            _layer=New("CAMetalLayer");
            Call(_layer,"setDevice:",Device);Call(_layer,"setPixelFormat:",80);Call(_layer,"setFramebufferOnly:",0);
            nint view=Require(Call(cocoaWindow,"contentView"),"NSWindow.contentView");
            Call(view,"setWantsLayer:",1);Call(view,"setLayer:",_layer);DrawableSize(_layer,_width,_height);
        }
        catch{Release(_layer);Release(_queue);Release(_pool);Release(Device);throw;}
    }
    internal void Track(MetalResource resource)=>_resources.Add(resource);
    internal void Untrack(MetalResource resource)=>_resources.Remove(resource);
    private void CheckAlive()=>ObjectDisposedException.ThrowIf(_disposed,this);
    private T Resource<T>(object value)where T:MetalResource
    {
        CheckAlive();if(value is not T r||r.Owner!=this)throw new ArgumentException("Resource belongs to a different graphics backend/device.");r.EnsureAlive();return r;
    }
    private MetalFramebuffer Framebuffer(IRenderFramebuffer value)
    {if(value is not MetalFramebuffer f||f.Owner!=this||!f.IsComplete)throw new ArgumentException("Invalid Metal framebuffer.");return f;}
    private MetalFramebuffer DefaultFramebuffer=>_backbuffer??=new(this,new MetalTexture(this,_width,_height,TextureFormat.Rgba8),DepthFormat.Depth24,_width,_height);
    private MetalFramebuffer DrawTarget=>_draw??DefaultFramebuffer;
    private MetalFramebuffer ReadTarget=>_read??DefaultFramebuffer;
    public IRenderTexture CreateTexture(int width,int height,TextureFormat format,ReadOnlySpan<byte> pixels)
    {
        CheckAlive();var t=new MetalTexture(this,width,height,format);
        try{if(!pixels.IsEmpty)t.Upload(pixels,checked(width*(format==TextureFormat.Rgb8?3:4)));return t;}catch{t.Dispose();throw;}
    }
    public IRenderTexture CreateRenderTexture(int width,int height,TextureFormat format)=>CreateTexture(width,height,format,ReadOnlySpan<byte>.Empty);
    public IRenderBuffer CreateBuffer(BufferKind kind,int sizeInBytes){CheckAlive();return new MetalBuffer(this,sizeInBytes);}
    public IRenderShader CreateShader(ShaderSource source){CheckAlive();ArgumentNullException.ThrowIfNull(source);return new MetalShader(this,source);}
    public IRenderFramebuffer CreateFramebuffer(IRenderTexture? color,DepthFormat depth,int width,int height){CheckAlive();return new MetalFramebuffer(this,color==null?null:Resource<MetalTexture>(color),depth,width,height);}
    public IRenderDepthSurface CreateDepthSurface(int width,int height,DepthFormat format){CheckAlive();return new MetalDepthSurface(this,width,height,format);}
    public void Resize(int framebufferWidth,int framebufferHeight)
    {
        CheckAlive();if(framebufferWidth<=0||framebufferHeight<=0)throw new ArgumentOutOfRangeException(nameof(framebufferWidth));
        if(_width==framebufferWidth&&_height==framebufferHeight)return;
        FlushFrameCommands();
        _backbuffer?.Dispose();_backbuffer?.Color?.Dispose();_backbuffer=null;
        _width=framebufferWidth;_height=framebufferHeight;_viewport=new(0,0,_width,_height,0,1);
        if(_layer!=0)DrawableSize(_layer,_width,_height);
    }
    public void AttachPresentSurface(int width,int height){CheckAlive();Resize(width,height);}
    public void BeginFrame()
    {
        CheckAlive();
        if(_frameOpen)throw new InvalidOperationException("A Metal frame is already open.");
        _framePool=New("NSAutoreleasePool");_frameOpen=true;
        FrameSubmissions = FrameDraws = FramePasses = 0;
        FrameWaitMilliseconds = 0;
        FrameUploadBytes = 0;
    }
    public void EndFrame()
    {
        CheckAlive();
        if(!_frameOpen)return;
        try { FlushFrameCommands(); }
        finally { Release(_framePool);_framePool=0;_frameOpen=false; }
    }
    public void SetPresentSource(IRenderTexture? texture)=>_presentSource=texture==null?null:Resource<MetalTexture>(texture);
    public void BindFramebuffer(IRenderFramebuffer? framebuffer){CheckAlive();_draw=_read=framebuffer==null?null:Framebuffer(framebuffer);}
    public void RestoreFramebufferBindings(IRenderFramebuffer? draw,IRenderFramebuffer? read){CheckAlive();_draw=draw==null?null:Framebuffer(draw);_read=read==null?null:Framebuffer(read);}
    public void SetReadBuffer(int colourAttachment){if(colourAttachment!=0)throw new ArgumentOutOfRangeException(nameof(colourAttachment));}
    public void AttachDepth(IRenderDepthSurface? depth)
    {
        CheckAlive();if(depth!=null&&(depth is not MetalDepthSurface d||d.Texture.Owner!=this||depth.Width!=DrawTarget.Width||depth.Height!=DrawTarget.Height))throw new ArgumentException("Metal depth attachment must belong to this device and match target dimensions.");
        DrawTarget.Depth=(MetalDepthSurface?)depth;
    }
    public (bool DepthTest,bool CullFace) QueryRasterState()=>(_depthTest,_cull!=CullMode.None);
    public void SetRasterState(bool depthTest,bool cullFace){_depthTest=depthTest;_cull=cullFace?CullMode.CounterClockwise:CullMode.None;}
    public void SetCapability(BackendCapability capability,bool enabled)
    {
        switch(capability){case BackendCapability.DepthTest:_depthTest=enabled;break;case BackendCapability.CullFace:_cull=enabled?CullMode.CounterClockwise:CullMode.None;break;case BackendCapability.ScissorTest:_scissorEnabled=enabled;break;case BackendCapability.Multisample:break;default:throw new ArgumentOutOfRangeException(nameof(capability));}
    }
    public void SetViewport(int x,int y,int width,int height,float minDepth,float maxDepth)
    {if(width<=0||height<=0||minDepth<0||maxDepth>1||minDepth>maxDepth)throw new ArgumentOutOfRangeException(nameof(width));_viewport=new(x,y,width,height,minDepth,maxDepth);}
    public void SetScissor(bool enabled,int x,int y,int width,int height){if(width<0||height<0)throw new ArgumentOutOfRangeException(nameof(width));_scissorEnabled=enabled;_scissor=(x,y,width,height);}
    public void SetDepthTest(bool enabled)=>_depthTest=enabled;
    public void SetDepthWrite(bool enabled)=>_depthWrite=enabled;
    public void SetCullMode(CullMode mode)=>_cull=mode;
    public void SetBlend(BlendState blend)=>_blend=blend;
    public void SetBlendState(BlendState state)=>SetBlend(state);
    public void SetSamplerState(SamplerStateBlock state)=>_sampling=state;
    public void BindTexture(IRenderTexture? texture)=>_texture=texture==null?null:Resource<MetalTexture>(texture);
    public void SetUniformBlock(int binding,IRenderBuffer? buffer,ReadOnlySpan<byte> data)
    {CheckAlive();if(binding<0||binding>=MetalShader.DefaultAttributeBufferIndex)throw new ArgumentOutOfRangeException(nameof(binding));if(buffer!=null)Resource<MetalBuffer>(buffer).Update(data,0);_uniforms[binding]=data.ToArray();}
    public bool TryGetError(out string message){message=string.Empty;return false;}
    public void Dispose()
    {
        if(_disposed)return;EndFrame();_disposed=true;
        if(_framePool!=0){Release(_framePool);_framePool=0;}_frameOpen=false;
        _backbuffer?.Dispose();
        foreach(var r in _resources.ToArray())r.Dispose();
        foreach(nint s in _samplers.Values)Release(s);foreach(nint s in _depthStates.Values)Release(s);
        Release(_presentPipeline);Release(_presentLibrary);Release(_layer);Release(_queue);Release(_pool);Release(Device);
    }
}
