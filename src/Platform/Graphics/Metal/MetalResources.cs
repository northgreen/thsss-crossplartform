#nullable enable
using System;
using static THSSS.Graphics.Metal.MetalNative;

namespace THSSS.Graphics.Metal;
internal sealed record MetalHandle(nint Value):IRenderHandle;
internal abstract class MetalResource:IDisposable
{
    internal nint Native{get;private set;}
    internal MetalRenderBackend Owner{get;}
    public IRenderHandle Handle=>new MetalHandle(Native);
    protected MetalResource(MetalRenderBackend owner,nint native){Owner=owner;Native=Require(native,GetType().Name);owner.Track(this);}
    internal void EnsureAlive()=>ObjectDisposedException.ThrowIf(Native==0,this);
    public virtual void Dispose(){if(Native==0)return;Release(Native);Native=0;Owner.Untrack(this);}
}
internal sealed unsafe class MetalBuffer:MetalResource,IRenderBuffer
{
    public int SizeInBytes{get;}
    internal MetalBuffer(MetalRenderBackend owner,int size):base(owner,Create(owner,size))=>SizeInBytes=size;
    private static nint Create(MetalRenderBackend owner,int size){ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);return Call(owner.Device,"newBufferWithLength:options:",size,0);}
    public void Update(ReadOnlySpan<byte> data,int offsetInBytes)
    {EnsureAlive();if(offsetInBytes<0||data.Length>SizeInBytes-offsetInBytes)throw new ArgumentOutOfRangeException(nameof(offsetInBytes));data.CopyTo(new Span<byte>((void*)(Call(Native,"contents")+offsetInBytes),data.Length));}
}
internal sealed unsafe class MetalTexture:MetalResource,IRenderTexture
{
    public int Width{get;}public int Height{get;}public TextureFormat Format{get;}
    internal SamplerStateBlock? Sampling{get;private set;}
    internal nint PixelFormat{get;}
    internal MetalTexture(MetalRenderBackend owner,int w,int h,TextureFormat format,nint depthFormat=0)
        :base(owner,Allocate(owner,w,h,depthFormat!=0?depthFormat:format==TextureFormat.Bgra8?80:70,depthFormat!=0))
    {Width=w;Height=h;Format=format;PixelFormat=depthFormat!=0?depthFormat:format==TextureFormat.Bgra8?80:70;}
    private static nint Allocate(MetalRenderBackend owner,int w,int h,nint format,bool depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(w);ArgumentOutOfRangeException.ThrowIfNegativeOrZero(h);
        nint d=New("MTLTextureDescriptor");
        try{Call(d,"setTextureType:",2);Call(d,"setPixelFormat:",format);Call(d,"setWidth:",w);Call(d,"setHeight:",h);Call(d,"setUsage:",depth?4:5);Call(d,"setStorageMode:",depth?2:0);return Call(owner.Device,"newTextureWithDescriptor:",d);}
        finally{Release(d);}
    }
    public void Upload(ReadOnlySpan<byte> pixels,int rowPitch)
    {
        EnsureAlive();int channels=Format==TextureFormat.Rgb8?3:4;
        if(rowPitch<Width*channels||pixels.Length<checked((Height-1)*rowPitch+Width*channels))throw new ArgumentException("Metal texture upload has insufficient rows or row pitch.");
        Owner.BeforeTextureUpload(this);
        if(channels==4){fixed(byte*p=pixels)MetalNative.Upload(Native,Width,Height,p,rowPitch);return;}
        byte[] rgba=new byte[checked(Width*Height*4)];
        for(int y=0;y<Height;y++)for(int x=0;x<Width;x++){int s=y*rowPitch+x*3,d=(y*Width+x)*4;rgba[d]=pixels[s];rgba[d+1]=pixels[s+1];rgba[d+2]=pixels[s+2];rgba[d+3]=255;}
        fixed(byte*p=rgba)MetalNative.Upload(Native,Width,Height,p,Width*4);
    }
    public void SetSamplerState(SamplerStateBlock state)=>Sampling=state;
}
internal sealed class MetalDepthSurface:IRenderDepthSurface
{
    internal MetalTexture Texture{get;}
    public IRenderHandle Handle=>Texture.Handle;
    public int Width=>Texture.Width;public int Height=>Texture.Height;public uint NativeAttachmentId=>0;
    internal MetalDepthSurface(MetalRenderBackend owner,int w,int h,DepthFormat format)
    {if(format==DepthFormat.None)throw new ArgumentException("A depth surface needs a depth format.",nameof(format));Texture=new(owner,w,h,TextureFormat.Rgba8,format==DepthFormat.Depth16?250:260);}
    public void Dispose()=>Texture.Dispose();
}
internal sealed class MetalFramebuffer:IRenderFramebuffer
{
    internal MetalRenderBackend Owner{get;}
    private readonly MetalDepthSurface? _ownedDepth;
    private MetalDepthSurface? _externalDepth;
    internal MetalDepthSurface? Depth{get=>_externalDepth??_ownedDepth;set=>_externalDepth=value;}
    private bool _disposed;
    public IRenderHandle Handle=>Color?.Handle??new MetalHandle(0);
    public int Width{get;}public int Height{get;}
    public bool IsComplete=>!_disposed&&(Color!=null||Depth!=null);
    public IRenderTexture? Color{get;}
    internal MetalFramebuffer(MetalRenderBackend owner,MetalTexture? color,DepthFormat depth,int w,int h)
    {
        if(w<=0||h<=0||(color!=null&&(color.Width!=w||color.Height!=h)))throw new ArgumentException("Metal framebuffer attachment dimensions do not match.");
        Owner=owner;Color=color;Width=w;Height=h;if(depth!=DepthFormat.None)_ownedDepth=new(owner,w,h,depth);
    }
    public void Dispose(){if(_disposed)return;_disposed=true;_ownedDepth?.Dispose();}
}
