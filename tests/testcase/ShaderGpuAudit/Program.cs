using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using SkiaSharp;
using SlimDX;
using SlimDX.Direct3D9;
using Texture = SlimDX.Direct3D9.Texture;
using D3DColor = SlimDX.Direct3D9.Color4;

var options = WindowOptions.Default;
options.Size = new Silk.NET.Maths.Vector2D<int>(64,64);
options.IsVisible = false;
using var window = Window.Create(options);
window.Initialize();
using var gl = window.CreateOpenGL();
using var device = new Device(gl,64,64) { GameWidth=64, GameHeight=64 };
using var target = new Texture(device,64,64,1,Usage.RenderTarget,Format.A8R8G8B8,Pool.Default);
using var surface = target.GetSurfaceLevel(0);
device.SetRenderTarget(0,surface);
byte[] Pixel(int x,int y)
{
    var pixel=new byte[4];
    unsafe { fixed(byte* p=pixel) gl.ReadPixels(x,y,1,1,Silk.NET.OpenGL.PixelFormat.Rgba,PixelType.UnsignedByte,p); }
    return pixel;
}
using var image = new SKBitmap(16,16, SKColorType.Rgba8888, SKAlphaType.Unpremul);
for(int y=0;y<16;y++) for(int x=0;x<16;x++) image.SetPixel(x,y,new SKColor(255,0,0,128));
using var translucent=Texture.FromImage(device,image);
using var sprite=new Sprite(device);
device.Clear(ClearFlags.Target,System.Drawing.Color.Blue,1,0);
sprite.Begin(SpriteFlags.AlphaBlend);
device.SetRenderState(RenderState.SourceBlend,Blend.SourceAlpha);
device.SetRenderState(RenderState.DestinationBlend,Blend.One);
sprite.Draw(translucent,null,null,Vector3.Zero,new D3DColor(1f,1f,1f,1f));
sprite.Transform=Matrix.Identity;
sprite.Draw(translucent,null,null,new Vector3(32,0,0),new D3DColor(1f,1f,1f,1f));
sprite.End();
var first=Pixel(8,56); var second=Pixel(40,56);
Assert(first.SequenceEqual(second),$"additive blend persists across Flush: first={string.Join(',',first)} second={string.Join(',',second)}");

using var whiteImage=new SKBitmap(1,1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
whiteImage.SetPixel(0,0,new SKColor(255,255,255,255));
using var white=Texture.FromImage(device,whiteImage);
using var vertices=new VertexBuffer(device,60,Usage.WriteOnly,VertexFormat.None,Pool.Managed);
device.SetTexture(0,white);
device.SetStreamSource(0,vertices,0,20);
device.SetTransform(TransformState.World,Matrix.Identity);
device.SetTransform(TransformState.View,Matrix.Identity);
device.SetTransform(TransformState.Projection,Matrix.Identity);
device.SetRenderState(RenderState.AlphaBlendEnable,false);
device.SetRenderState(RenderState.ZEnable,true);
using var depth=Surface.CreateDepthStencil(device,64,64,Format.D16,MultiSampleType.None,0,true);
device.DepthStencilSurface=depth;
device.Clear(ClearFlags.Target|ClearFlags.ZBuffer,System.Drawing.Color.Black,1,0);
void Triangle(float z,D3DColor colour)
{
    var stream=vertices.Lock(0,60,LockFlags.Discard);
    stream.WriteRange(new float[]{-1,-1,z,0,0, 1,-1,z,0,0, 0,1,z,0,0});
    vertices.Unlock();
    device.Material=new Material{Diffuse=colour};
    device.DrawPrimitives(SlimDX.Direct3D9.PrimitiveType.TriangleList,0,1);
}
device.SetRenderState(RenderState.ZEnable,false);
Console.WriteLine($"[NODEPTH] target={device.GetRenderTarget(0)?.Width} stream={100}");
Triangle(0.2f,new D3DColor(1f,0f,0f,1f));
Console.WriteLine($"[NODEPTH] red={string.Join(',',Pixel(32,32))}");
device.SetRenderState(RenderState.ZEnable,true);
device.Clear(ClearFlags.Target|ClearFlags.ZBuffer,System.Drawing.Color.Black,1,0);
Triangle(0.2f,new D3DColor(1f,0f,0f,1f));
Triangle(0.8f,new D3DColor(0f,0f,1f,1f));
Console.WriteLine($"[DEPTH] fbo={surface.FBO} drawfb={gl.GetInteger(GLEnum.DrawFramebufferBinding)} depthTest={gl.IsEnabled(Silk.NET.OpenGL.EnableCap.DepthTest)} depthMask={(byte)gl.GetInteger(GLEnum.DepthWritemask)} err={gl.GetError()}");
var depthPixel=Pixel(32,32);
Console.WriteLine($"[DEPTH] pixel={string.Join(',',depthPixel)}");
Assert(depthPixel[0]>240 && depthPixel[2]<15,$"depth retains nearer red over farther blue: actual={string.Join(',',depthPixel)}");
device.SetRenderTarget(0,null);
Console.WriteLine("PASS: former shader audit defects now enforced as regression assertions");

// Regression assertions for the remaining fixed-function findings.
void Assert(bool value,string message)
{
    if (!value) throw new Exception(message);
    Console.WriteLine("PASS: " + message);
}
device.SetRenderTarget(0,surface);
device.SetRenderState(RenderState.ZEnable,false);
device.SetRenderState(RenderState.AlphaBlendEnable,false);
device.Clear(ClearFlags.Target,System.Drawing.Color.Blue,1,0);
device.Viewport=new Viewport(16,16,32,32,0,1);
Triangle(0.5f,new D3DColor(1f,0f,0f,1f));
Assert(Pixel(32,32)[0]>240 && Pixel(8,32)[2]>240,"local viewport preserves outside pixels");
device.Viewport=new Viewport(0,0,64,64,0,1);
device.SetRenderState(RenderState.ZEnable,true);
device.SetRenderState(RenderState.ZWriteEnable,false);
device.Clear(ClearFlags.Target|ClearFlags.ZBuffer,System.Drawing.Color.Black,1,0);
device.SetRenderState(RenderState.ZEnable,false);
Console.WriteLine($"[NODEPTH] target={device.GetRenderTarget(0)?.Width} stream={100}");
Triangle(0.2f,new D3DColor(1f,0f,0f,1f));
Console.WriteLine($"[NODEPTH] red={string.Join(',',Pixel(32,32))}");
device.SetRenderState(RenderState.ZEnable,true);
device.Clear(ClearFlags.Target|ClearFlags.ZBuffer,System.Drawing.Color.Black,1,0);
Triangle(0.2f,new D3DColor(1f,0f,0f,1f));
Triangle(0.8f,new D3DColor(0f,0f,1f,1f));
Assert(Pixel(32,32)[2]>240,"ZWriteEnable=false leaves depth unchanged");
device.SetRenderState(RenderState.ZWriteEnable,true);
device.SetRenderState(RenderState.ZEnable,false);
device.SetRenderState(RenderState.AlphaBlendEnable,true);
device.SetRenderState(RenderState.SourceBlend,Blend.One);
device.SetRenderState(RenderState.DestinationBlend,Blend.One);
device.SetRenderState(RenderState.BlendOperation,BlendOperation.ReverseSubtract);
device.Clear(ClearFlags.Target,System.Drawing.Color.Blue,1,0);
Triangle(0.5f,new D3DColor(1f,0f,0f,1f));
var subtract=Pixel(32,32);
Assert(subtract[0]<10 && subtract[2]>240,"ReverseSubtract uses destination minus source");
device.SetRenderState(RenderState.BlendOperation,BlendOperation.Add);
device.SetRenderState(RenderState.AlphaBlendEnable,false);
using var normals=new VertexBuffer(device,96,Usage.WriteOnly,VertexFormat.Position|VertexFormat.Normal|VertexFormat.Texture1,Pool.Managed);
var normalData=normals.Lock(0,96,LockFlags.Discard);
normalData.WriteRange(new float[]{-1,-1,0.5f,0,0,1,0,0, 1,-1,0.5f,0,0,1,0,0, 0,1,0.5f,0,0,1,0,0});
normals.Unlock();
device.SetStreamSource(0,normals,0,32);
device.SetFVF(VertexFormat.Position|VertexFormat.Normal|VertexFormat.Texture1);
device.Material=new Material{Diffuse=new D3DColor(1f,0f,0f,1f)};
device.SetRenderState(RenderState.Lighting,true);
device.EnableLight(0,true);
device.SetLight(0,new Light{Type=LightType.Directional,Diffuse=System.Drawing.Color.White,Direction=new Vector3(0,0,-1)});
device.DrawPrimitives(SlimDX.Direct3D9.PrimitiveType.TriangleList,0,1);
Assert(Pixel(32,32)[0]>240,"directional diffuse lighting consumes mesh normals");
device.SetLight(0,new Light{Type=LightType.Directional,Diffuse=System.Drawing.Color.White,Direction=new Vector3(0,0,1)});
device.DrawPrimitives(SlimDX.Direct3D9.PrimitiveType.TriangleList,0,1);
Assert(Pixel(32,32)[0]<10,"opposite directional light does not illuminate back-facing normals");
device.SetRenderState(RenderState.Lighting,false);
using var colored=new VertexBuffer(device,72,Usage.WriteOnly,VertexFormat.Position|VertexFormat.Diffuse|VertexFormat.Texture1,Pool.Managed);
var colorData=colored.Lock(0,72,LockFlags.Discard);
foreach(var xy in new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(0,1)})
{
    colorData.Write(new Vector3(xy.X,xy.Y,0.5f));
    colorData.Write(unchecked((int)0xff00ff00));
    colorData.Write(Vector2.Zero);
}
colored.Unlock();
device.SetStreamSource(0,colored,0,24);
device.SetFVF(VertexFormat.Position|VertexFormat.Diffuse|VertexFormat.Texture1);
device.DrawPrimitives(SlimDX.Direct3D9.PrimitiveType.TriangleList,0,1);
var green=Pixel(32,32);
Assert(green[0]<10 && green[1]>240 && green[2]<10,"packed vertex colour layout is decoded as D3DCOLOR");
device.SetRenderTarget(0,null);

// Sampler addressing is device state, independent of the texture's upload defaults.
device.SetRenderTarget(0,surface);
device.SetRenderState(RenderState.AlphaBlendEnable,false);
device.SetRenderState(RenderState.Lighting,false);
device.Material=new Material{Diffuse=new D3DColor(1f,1f,1f,1f)};
using var bands=new SKBitmap(2,1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
bands.SetPixel(0,0,new SKColor(255,0,0,255)); bands.SetPixel(1,0,new SKColor(0,255,0,255));
using var bandTexture=Texture.FromImage(device,bands);
device.SetTexture(0,bandTexture);
device.SetStreamSource(0,vertices,0,20);
device.SetFVF(VertexFormat.Position|VertexFormat.Texture1);
void SampleAt(float u)
{
    var d=vertices.Lock(0,60,LockFlags.Discard);
    d.WriteRange(new float[]{-1,-1,0.5f,u,0.5f,1,-1,0.5f,u,0.5f,0,1,0.5f,u,0.5f});
    vertices.Unlock();
    device.DrawPrimitives(SlimDX.Direct3D9.PrimitiveType.TriangleList,0,1);
}
foreach(var address in new[]{TextureAddress.Wrap,TextureAddress.Clamp,TextureAddress.Mirror})
{
    device.SetSamplerState(0,SamplerState.AddressU,address);
    SampleAt(1.25f);
    var sampled=Pixel(32,32);
    Assert(address==TextureAddress.Wrap ? sampled[0]>240 && sampled[1]<10 : sampled[1]>240 && sampled[0]<10,"sampler address "+address);
}
device.SetSamplerState(0,SamplerState.AddressU,TextureAddress.Clamp);
device.SetSamplerState(0,SamplerState.MinFilter,TextureFilter.Linear);
device.SetSamplerState(0,SamplerState.MagFilter,TextureFilter.Linear);
SampleAt(0.5f);
var linear=Pixel(32,32);
Assert(Math.Abs(linear[0]-128)<5 && Math.Abs(linear[1]-128)<5,"sampler linear filtering mixes adjacent texels");
device.SetSamplerState(0,SamplerState.AddressU,TextureAddress.Border);
device.SetSamplerState(0,SamplerState.BorderColor,unchecked((int)0xff0000ff));
SampleAt(1.25f);
Assert(Pixel(32,32)[2]>240,"sampler border color preserves D3DCOLOR channels");
device.SetRenderTarget(0,null);

device.SetRenderTarget(0,surface);
device.SetTexture(0,white);
device.SetSamplerState(0,SamplerState.AddressU,TextureAddress.Wrap);
var declaredData=colored.Lock(0,72,LockFlags.Discard);
foreach(var xy in new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(0,1)})
{
    declaredData.Write(new Vector3(xy.X,xy.Y,0.5f));
    declaredData.Write(unchecked((int)0xffff0000));
    declaredData.Write(Vector2.Zero);
}
colored.Unlock();
using var declaration=new VertexDeclaration(device,new[]{
    new VertexElement(0,0,DeclarationType.Float3,DeclarationMethod.Default,DeclarationUsage.Position,0),
    new VertexElement(0,16,DeclarationType.Float2,DeclarationMethod.Default,DeclarationUsage.TexCoord,0),
    new VertexElement(0,12,DeclarationType.Color,DeclarationMethod.Default,DeclarationUsage.Color,0),
    VertexElement.VertexDeclarationEnd});
device.SetVertexDeclaration(declaration);
device.SetStreamSource(0,colored,0,24);
device.DrawPrimitives(SlimDX.Direct3D9.PrimitiveType.TriangleList,0,1);
var declaredRed=Pixel(32,32);
Assert(declaredRed[0]>240 && declaredRed[1]<10 && declaredRed[2]<10,"vertex declaration offsets and packed red/blue channel order");
var specData=normals.Lock(0,96,LockFlags.Discard);
specData.WriteRange(new float[]{-1,-1,0.5f,0,0,-1,0,0, 1,-1,0.5f,0,0,-1,0,0, 0,1,0.5f,0,0,-1,0,0});
normals.Unlock();
device.SetStreamSource(0,normals,0,32);
device.SetFVF(VertexFormat.Position|VertexFormat.Normal|VertexFormat.Texture1);
device.SetRenderState(RenderState.Lighting,true);
device.SetRenderState(RenderState.LocalViewer,false);
using var blackImage=new SKBitmap(1,1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
blackImage.SetPixel(0,0,new SKColor(0,0,0,255));
using var blackTexture=Texture.FromImage(device,blackImage);
device.SetTexture(0,blackTexture);
device.Material=new Material{Diffuse=new D3DColor(0f,0f,0f,1f),Specular=new D3DColor(0f,0f,1f,1f),Power=16};
device.SetLight(0,new Light{Type=LightType.Directional,Specular=System.Drawing.Color.White,Direction=new Vector3(0,0,1)});
foreach(bool specular in new[]{false,true})
{
    device.SetRenderState(RenderState.SpecularEnable,specular);
    device.DrawPrimitives(SlimDX.Direct3D9.PrimitiveType.TriangleList,0,1);
    Assert(specular ? Pixel(32,32)[2]>240 : Pixel(32,32)[2]<10,"specular material highlight enable="+specular);
}
device.SetRenderState(RenderState.MultisampleAntialias,true);
Assert(gl.IsEnabled(Silk.NET.OpenGL.EnableCap.Multisample),"MultisampleAntialias enables GL multisample state");
device.SetRenderState(RenderState.MultisampleAntialias,false);
Assert(!gl.IsEnabled(Silk.NET.OpenGL.EnableCap.Multisample),"MultisampleAntialias disables GL multisample state");
device.SetRenderTarget(0,null);

// Review regressions: declaration stride may include padding and FVF changes must clear it.
device.SetRenderTarget(0,surface);
device.SetRenderState(RenderState.Lighting,false);
device.SetRenderState(RenderState.SpecularEnable,false);
device.SetTexture(0,white);
using var padded=new VertexBuffer(device,84,Usage.WriteOnly,VertexFormat.None,Pool.Managed);
var pd=padded.Lock(0,84,LockFlags.Discard);
foreach(var xy in new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(0,1)})
{ pd.Write(new Vector3(xy.X,xy.Y,0.5f)); pd.Write(unchecked((int)0xff00ff00)); pd.Write(Vector2.Zero); pd.Write(0f); }
padded.Unlock();
device.VertexDeclaration=declaration;
device.SetStreamSource(0,padded,0,28);
device.DrawPrimitives(SlimDX.Direct3D9.PrimitiveType.TriangleList,0,1);
Assert(Pixel(32,32)[1]>240,"padded stream uses declaration offsets independent of stride");
device.VertexFormat=VertexFormat.Position|VertexFormat.Texture1;
Assert(device.VertexDeclaration==null,"direct FVF property clears stale declaration");
device.SetStreamSource(0,vertices,0,20);
device.Material=new Material{Diffuse=new D3DColor(1f,1f,1f,1f)};
device.SetTexture(0,bandTexture);
device.SetSamplerState(0,SamplerState.AddressU,TextureAddress.Clamp);
device.SetSamplerState(0,SamplerState.MipFilter,TextureFilter.Linear);
SampleAt(0.25f);
Assert(Pixel(32,32)[0]>240,"single-level texture remains complete under mip sampling");
device.SetSamplerState(0,SamplerState.MipFilter,TextureFilter.None);
using(var anotherSurface=new Surface(gl,8,8,true))
{
    gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer,anotherSurface.FBO);
    gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer,surface.FBO);
    using var independent=new Surface(gl,4,4,true);
    Assert(gl.GetInteger(GLEnum.ReadFramebufferBinding)==anotherSurface.FBO && gl.GetInteger(GLEnum.DrawFramebufferBinding)==surface.FBO,
        "Surface creation preserves distinct read and draw framebuffer bindings");
}
gl.BindFramebuffer(FramebufferTarget.Framebuffer,surface.FBO);
device.SetTexture(0,blackTexture);
device.SetStreamSource(0,normals,0,32);
device.SetFVF(VertexFormat.Position|VertexFormat.Normal|VertexFormat.Texture1);
device.SetRenderState(RenderState.Lighting,true);
device.SetRenderState(RenderState.SpecularEnable,true);
device.Material=new Material{Diffuse=new D3DColor(0f,0f,0f,1f),Specular=new D3DColor(0f,0f,1f,1f),Power=16};
device.SetRenderState(RenderState.LocalViewer,false);
device.DrawPrimitives(SlimDX.Direct3D9.PrimitiveType.TriangleList,0,1);
byte infiniteViewer=Pixel(32,32)[2];
device.SetRenderState(RenderState.LocalViewer,true);
device.DrawPrimitives(SlimDX.Direct3D9.PrimitiveType.TriangleList,0,1);
Assert(infiniteViewer>240 && Pixel(32,32)[2]<infiniteViewer-20,"local viewer uses vertex-to-eye direction rather than fixed infinite viewer");
device.SetRenderTarget(0,null);

device.SetRenderTarget(0,surface);
device.SetRenderState(RenderState.Lighting,false);
device.SetRenderState(RenderState.ZEnable,false);
device.SetRenderState(RenderState.AlphaBlendEnable,false);
device.Material=new Material{Diffuse=new D3DColor(1f,0f,0f,1f)};
device.SetTexture(0,white);
device.DrawUserPrimitives(SlimDX.Direct3D9.PrimitiveType.TriangleList,1,new[]{
    new AuditVertex(-1,-1,0.5f),new AuditVertex(1,-1,0.5f),new AuditVertex(0,1,0.5f)},
    VertexFormat.Position|VertexFormat.Texture1);
Assert(Pixel(32,32)[0]>240,"DrawUserPrimitives uploads real vertex data");
device.SetRenderTarget(0,null);

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
struct AuditVertex
{
    public float X,Y,Z,U,V;
    public AuditVertex(float x,float y,float z) { X=x;Y=y;Z=z;U=V=0; }
}
