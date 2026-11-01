using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using SkiaSharp;
using SlimDX;
using SlimDX.Direct3D9;
using Texture = SlimDX.Direct3D9.Texture;

// Headless 64x64 GL window: every assertion below is a GPU readback, never CPU math.
// A software rasteriser is acceptable here - that proves the port draws the same
// thing it uploaded, not that any D3D9 driver would have produced these pixels.
var options = WindowOptions.Default;
options.Size = new Silk.NET.Maths.Vector2D<int>(64, 64);
options.IsVisible = false;
using var window = Window.Create(options);
window.Initialize();
using var gl = window.CreateOpenGL();
var device = new Device(gl, 64, 64) { GameWidth = 64, GameHeight = 64 };
UniformBufferReadback.Run(gl, device.RenderBackend);
// Source rows are top-down: row 0 is red/green, row 1 is blue/white.
// The upload preserves that order in GL row 0 (no flip on upload). Every V flip in
// this port happens later, in sprite UV generation or render-target orientation.
using var image = new SKBitmap(2, 2, SKColorType.Rgba8888, SKAlphaType.Unpremul);
image.SetPixel(0, 0, new SKColor(255, 0, 0, 255));
image.SetPixel(1, 0, new SKColor(0, 255, 0, 255));
image.SetPixel(0, 1, new SKColor(0, 0, 255, 255));
image.SetPixel(1, 1, new SKColor(255, 255, 255, 255));
using var texture = Texture.FromImage(device, image);
// GetTexImage writes the ENTIRE texture: allocate all 2*2*4 bytes, not one pixel.
var bytes = new byte[16];
gl.BindTexture(TextureTarget.Texture2D, texture.GLName);
unsafe { fixed (byte* p = bytes) gl.GetTexImage(TextureTarget.Texture2D, 0, Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, p); }
// Byte 0 is still the source top-left red. D3D9 and GL agree on raw row order and
// only disagree about which row V=0 addresses, so a flipped upload is the bug this
// pins down. Note the pixel order is RGBA here: the A8R8G8B8 source becomes RGBA8.
if (bytes[0] != 255 || bytes[1] != 0 || bytes[2] != 0)
    throw new Exception($"Texture row zero must retain source top-left red; got {bytes[0]},{bytes[1]},{bytes[2]}");
// A8R8G8B8 is D3D9's 32-bit colour format; the port maps it to RGBA8 internally, which
// is why every GetTexImage/ReadPixels call below reads RGBA and never BGRA.
// The two FBO-binding probes guard a real hazard: a render-target constructor that
// leaves its framebuffer bound would silently redirect the next draw.
using var target = new Texture(device, 64, 64, 1, Usage.RenderTarget, Format.A8R8G8B8, Pool.Default);
using var surface = target.GetSurfaceLevel(0);
if (surface.FBO == 0) throw new Exception("Render target has no FBO");
if (gl.GetInteger(GLEnum.FramebufferBinding) != 0) throw new Exception("Surface construction leaked binding");
device.SetRenderTarget(0, surface);
using var sprite = new Sprite(device);
sprite.Begin(SpriteFlags.AlphaBlend);
sprite.Transform = Matrix.Scaling(32, 32, 1);
sprite.Draw(texture, null, null, Vector3.Zero, new SlimDX.Direct3D9.Color4(1f, 1f, 1f, 1f));
sprite.Transform = Matrix.Identity;
sprite.End();
var pixel = new byte[4];
unsafe { fixed (byte* p = pixel) gl.ReadPixels(8, 56, 1, 1, Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, p); }
// glReadPixels row 0 is the BOTTOM of the framebuffer, so y=56 of 64 is the top edge.
// The drawn sprite must therefore put the red texel at the top: V is flipped at draw
// time, not at upload. The 240/15 thresholds absorb driver colour rounding only.
if (pixel[0] < 240 || pixel[1] > 15 || pixel[2] > 15)
    throw new Exception($"Rendered top-left must be red; got {string.Join(',', pixel)}");
device.SetRenderTarget(0, null);
if (gl.GetInteger(GLEnum.FramebufferBinding) != 0) throw new Exception("Backbuffer restoration failed");
using var secondTarget = new Texture(device, 64, 64, 1, Usage.RenderTarget, Format.A8R8G8B8, Pool.Default);
using var secondSurface = secondTarget.GetSurfaceLevel(0);
device.SetRenderTarget(0, secondSurface);
sprite.Begin(SpriteFlags.AlphaBlend);
sprite.Draw(target, null, null, Vector3.Zero, new SlimDX.Direct3D9.Color4(1f, 1f, 1f, 1f));
sprite.End();
unsafe { fixed (byte* p = pixel) gl.ReadPixels(8, 56, 1, 1, Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, p); }
// Reading the first render target back through a sprite exercises the double flip:
// the target is stored bottom-up in GL, so sampling it must flip once more to land
// red at the top again. Without that flip this pixel would still be blue.
if (pixel[0] < 240 || pixel[1] > 15 || pixel[2] > 15)
    throw new Exception($"Composited render target top-left must remain red; got {string.Join(',', pixel)}");
device.SetRenderTarget(0, null);
Console.WriteLine("PASS: texture row orientation, FBO creation/restoration, transformed sprite pixel");

using var bitmap = new System.Drawing.Bitmap(128, 64);
using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
using (var font = new System.Drawing.Font("楷体", 16f, System.Drawing.FontStyle.Bold))
{
    graphics.DrawString("夏夜对话", font, System.Drawing.Brushes.White, 0, 0);
}
using var textTexture = new Texture(device, 128, 64, 1, Usage.None, Format.A8R8G8B8, Pool.Managed);
var locked = textTexture.LockRectangle(0, LockFlags.Discard);
var bitmapData = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, 128, 64),
    System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);
locked.Data.WriteRange(bitmapData.Scan0, 128 * 64 * 4);
bitmap.UnlockBits(bitmapData);
textTexture.UnlockRectangle(0);
var textPixels = new byte[128 * 64 * 4];
gl.BindTexture(TextureTarget.Texture2D, textTexture.GLName);
unsafe { fixed (byte* p = textPixels) gl.GetTexImage(TextureTarget.Texture2D, 0, Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, p); }
int opaqueText = 0;
for (int i = 3; i < textPixels.Length; i += 4)
    if (textPixels[i] > 0) opaqueText++;
if (opaqueText < 100) throw new Exception($"Dialogue text upload is empty: {opaqueText} pixels");
Console.WriteLine($"PASS: dialogue Bitmap -> LockBits -> DataStream -> texture upload ({opaqueText} visible pixels)");

using var modelTarget = new Texture(device, 64, 64, 1, Usage.RenderTarget, Format.A8R8G8B8, Pool.Default);
using var modelSurface = modelTarget.GetSurfaceLevel(0);
device.SetRenderTarget(0, modelSurface);
device.Clear(ClearFlags.Target, System.Drawing.Color.Black, 1, 0);
using var modelVertices = new VertexBuffer(device, 60, Usage.WriteOnly, VertexFormat.None, Pool.Default);
var modelData = modelVertices.Lock(0, 60, LockFlags.None);
modelData.WriteRange(new float[] { -1,-1,0,0,0, 1,-1,0,0,0, 0,1,0,0,0 });
modelVertices.Unlock();
device.SetStreamSource(0, modelVertices, 0, 20);
device.SetTexture(0, texture);
var material = new Material { Diffuse = new SlimDX.Direct3D9.Color4(1f,1f,1f,1f) };
device.SetMaterial(ref material);
device.SetRenderState(RenderState.SourceBlend, 2);
device.SetRenderState(RenderState.DestinationBlend, 1);
device.SetTransform(TransformState.World, Matrix.Identity);
device.SetTransform(TransformState.View, Matrix.LookAtLH(new Vector3(0,0,-2), Vector3.Zero, Vector3.UnitY));
device.SetTransform(TransformState.Projection, Matrix.PerspectiveFovLH(MathF.PI / 2, 1, 1, 10));
device.DrawPrimitives(SlimDX.Direct3D9.PrimitiveType.TriangleList, 0, 1);
unsafe { fixed (byte* p = pixel) gl.ReadPixels(32, 32, 1, 1, Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, p); }
if (pixel[0] < 200 || pixel[1] > 30 || pixel[2] > 30)
    throw new Exception($"3D camera triangle must be red at screen centre: {string.Join(',',pixel)}");
device.SetRenderTarget(0, null);
Console.WriteLine("PASS: real Device3D camera, VBO and textured triangle produce red centre pixel");

device.SetRenderTarget(0, modelSurface);
// The triangle spans view depth 1..3 and its centre sits at depth 2. With FogStart 1.5
// and FogEnd 2.5 the linear factor is exactly 0.5, so red and blue must average to 128.
// Fixing the geometry makes the expected number derivable, not observed-and-blessed.
device.SetRenderState(RenderState.FogEnable, true);
device.SetRenderState(RenderState.FogTableMode, SlimDX.Direct3D9.FogMode.Linear);
device.SetRenderState(RenderState.FogColor, System.Drawing.Color.Blue.ToArgb());
device.SetRenderState(RenderState.FogStart, 1.5f);
device.SetRenderState(RenderState.FogEnd, 2.5f);
device.DrawPrimitives(SlimDX.Direct3D9.PrimitiveType.TriangleList, 0, 1);
unsafe { fixed (byte* p = pixel) gl.ReadPixels(32,32,1,1,Silk.NET.OpenGL.PixelFormat.Rgba,PixelType.UnsignedByte,p); }
if (Math.Abs(pixel[0]-128) > 5 || pixel[1] > 5 || Math.Abs(pixel[2]-128) > 5)
    throw new Exception($"Half-depth fog must mix red and blue equally: {string.Join(',',pixel)}");
device.SetRenderState(RenderState.FogEnable, false);
// Fixed-function ambient: white ambient material modulated by a grey (128) light gives
// 0.5, i.e. red 255 -> 128. Halving the diffuse texture with an "ambient" light looks
// odd, but D3D9's ambient sum does exactly this and the game's billboards rely on it.
material.Ambient = new SlimDX.Direct3D9.Color4(1f,1f,1f,1f);
device.Material = material;
var light = new Light { Ambient = System.Drawing.Color.FromArgb(128,128,128) };
device.SetLight(0, light);
device.EnableLight(0,true);
device.SetRenderState(RenderState.Lighting,true);
device.DrawPrimitives(SlimDX.Direct3D9.PrimitiveType.TriangleList,0,1);
unsafe { fixed(byte* p=pixel) gl.ReadPixels(32,32,1,1,Silk.NET.OpenGL.PixelFormat.Rgba,PixelType.UnsignedByte,p); }
if (Math.Abs(pixel[0]-128)>5 || pixel[1]>5 || pixel[2]>5)
    throw new Exception($"Stage billboard ambient light must halve red: {string.Join(',',pixel)}");
device.SetRenderState(RenderState.Lighting,false);
device.SetRenderTarget(0,null);
// Raw D3DBLEND values, asserted as integers because the game passes literals such as
// SetRenderState(RenderState.SourceBlend, 2) in places. If the enum ever gains a
// different underlying numbering, those literal call sites silently change meaning.
if ((int)Blend.SourceAlpha != 5 || (int)Blend.InverseSourceAlpha != 6 || (int)Blend.One != 2)
    throw new Exception("Blend enum no longer matches raw D3D9 state values");
Console.WriteLine("PASS: fractional linear fog, ambient lighting and D3D blend constants");

device.SetRenderTarget(0,modelSurface);
material.Diffuse = new SlimDX.Direct3D9.Color4(1f,1f,1f,0.5f);
device.Material = material;
device.SetRenderState(RenderState.SourceBlend,Blend.SourceAlpha);
device.SetRenderState(RenderState.DestinationBlend,Blend.InverseSourceAlpha);
// Diffuse alpha 0.5 with SrcAlpha/InvSrcAlpha over blue must give 128/128 when blending
// is on and the raw 255/0 source when it is off. The port does not premultiply: the
// colour reaching the blend unit is exactly the material colour, as in D3D9.
foreach (bool blending in new[] {false,true})
{
    device.Clear(ClearFlags.Target,System.Drawing.Color.Blue,1,0);
    device.SetRenderState(RenderState.AlphaBlendEnable,blending);
    device.DrawPrimitives(SlimDX.Direct3D9.PrimitiveType.TriangleList,0,1);
    unsafe { fixed(byte* p=pixel) gl.ReadPixels(32,32,1,1,Silk.NET.OpenGL.PixelFormat.Rgba,PixelType.UnsignedByte,p); }
    int expectedR = blending ? 128 : 255;
    int expectedB = blending ? 128 : 0;
    if(Math.Abs(pixel[0]-expectedR)>5 || Math.Abs(pixel[2]-expectedB)>5)
        throw new Exception($"AlphaBlendEnable={blending} mismatch: {string.Join(',',pixel)}");
}
device.SetRenderTarget(0,null);
Console.WriteLine("PASS: 3D drawing preserves AlphaBlendEnable on/off");

// The additional fog equations share the original fixed-function depth input.
device.SetRenderTarget(0,modelSurface);
device.SetTexture(0,texture);
device.SetStreamSource(0,modelVertices,0,20);
device.SetFVF(VertexFormat.Position|VertexFormat.Texture1);
device.Material=new Material{Diffuse=new SlimDX.Direct3D9.Color4(1f,1f,1f,1f)};
device.SetRenderState(RenderState.AlphaBlendEnable,false);
device.SetRenderState(RenderState.Lighting,false);
device.SetRenderState(RenderState.FogEnable,true);
device.SetRenderState(RenderState.FogColor,System.Drawing.Color.Blue.ToArgb());
device.SetRenderState(RenderState.FogDensity,0.5f);
foreach(var mode in new[]{SlimDX.Direct3D9.FogMode.Exponential,SlimDX.Direct3D9.FogMode.ExponentialSquared})
{
    device.SetRenderState(RenderState.FogTableMode,mode);
    device.DrawPrimitives(SlimDX.Direct3D9.PrimitiveType.TriangleList,0,1);
    unsafe { fixed(byte* p=pixel) gl.ReadPixels(32,32,1,1,Silk.NET.OpenGL.PixelFormat.Rgba,PixelType.UnsignedByte,p); }
    // Depth 2 with density 0.5 makes depth*density = 1, and at that point D3D9's Exp and
    // Exp2 formulas collapse to the same e^-1. The single constant is deliberate: one
    // loop covers both modes because their inputs coincide here, not by approximation.
    float factor=MathF.Exp(-1);
    if(Math.Abs(pixel[0]-255*factor)>5 || Math.Abs(pixel[2]-255*(1-factor))>5)
        throw new Exception($"Fog mode {mode} pixel mismatch: {string.Join(',',pixel)}");
}
device.SetRenderState(RenderState.FogEnable,false);
device.SetRenderTarget(0,null);
Console.WriteLine("PASS: Exp and Exp2 fog equations match GPU depth samples");
