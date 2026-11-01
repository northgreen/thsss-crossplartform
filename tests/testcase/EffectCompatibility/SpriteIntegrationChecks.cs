using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using SkiaSharp;
using SlimDX;
using SlimDX.Direct3D9;
using Texture = SlimDX.Direct3D9.Texture;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

internal static class SpriteIntegrationChecks
{
    internal static unsafe void Run()
    {
        const int width=96,height=80;
        var options=WindowOptions.Default;
        options.Size=new Silk.NET.Maths.Vector2D<int>(width,height);
        options.IsVisible=false;
        using var window=Window.Create(options); window.Initialize();
        using var gl=window.CreateOpenGL();
        using var device=new Device(gl,width,height){GameWidth=width,GameHeight=height};
        using var image=new SKBitmap(width,height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        for(int y=0;y<height;y++) for(int x=0;x<width;x++)
            image.SetPixel(x,y,new SKColor((byte)(60+x),(byte)(80+y),(byte)(70+x/2+y/2),255));
        using var uploaded=Texture.FromImage(device,image);
        using var scene=new Texture(device,width,height,1,Usage.RenderTarget,Format.A8R8G8B8,Pool.Managed);
        using var sceneSurface=scene.GetSurfaceLevel(0);
        using var output=new Texture(device,width,height,1,Usage.RenderTarget,Format.A8R8G8B8,Pool.Managed);
        using var outputSurface=output.GetSurfaceLevel(0);
        using var sprite=new Sprite(device);
        device.SetRenderTarget(0,sceneSurface);
        sprite.Begin(SpriteFlags.None);
        sprite.Draw(uploaded,null,null,Vector3.Zero,new Color4(1f,1f,1f,1f));
        sprite.End();
        using var filter=Effect.FromFile(device,"../game/FX/Filter.fx");
        using var blur=Effect.FromFile(device,"../game/FX/Blur.fx");
        using var warp=Effect.FromFile(device,"../game/FX/Warp.fx");
        void Test(Effect effect,Func<NVector2,NVector4> reference,string name,bool explicitSource=false)
        {
            device.SetRenderTarget(0,outputSurface);
            device.Clear(ClearFlags.Target,System.Drawing.Color.Black,1,0);
            if(explicitSource) effect.SetTexture("xColoredTexture",scene);
            effect.Begin(); effect.BeginPass(0);
            sprite.Begin(SpriteFlags.None);
            sprite.Draw(explicitSource ? uploaded : scene,null,null,Vector3.Zero,new Color4(0.1f,0.2f,0.3f,0.4f));
            sprite.End(); effect.EndPass(); effect.End();
            var actual=new byte[width*height*4];
            fixed(byte* p=actual) gl.ReadPixels(0,0,width,height,Silk.NET.OpenGL.PixelFormat.Rgba,PixelType.UnsignedByte,p);
            float maxError=0;
            for(int y=0;y<height;y++) for(int x=0;x<width;x++)
            {
                var expected=reference(new NVector2((x+0.5f)/width,(y+0.5f)/height));
                for(int c=0;c<4;c++)
                {
                    float observed=actual[((height-1-y)*width+x)*4+c]/255f;
                    float error=MathF.Abs(observed-Math.Clamp(expected[c],0,1));
                    maxError=Math.Max(maxError,error);
                    if(error>0.008f) throw new Exception($"{name}:({x},{y}) channel{c} expected={expected[c]} actual={observed}");
                }
            }
            Console.WriteLine($"PASS Sprite/RT integration {name}: all RGBA, maxError={maxError:F6}");
        }
        Test(filter,uv=>Reference.Filter(image,uv),"Filter");
        Test(filter,uv=>Reference.Filter(image,uv),"explicit RT texture over uploaded source",true);
        blur.SetFloat("BlurLevel",0.0025f);
        Test(blur,uv=>Reference.Blur(image,uv,0.0025f),"Blur asymmetric image");
        warp.SetFloat("X",0.28f); warp.SetFloat("Y",0.21f); warp.SetFloat("Radius",0.18f); warp.SetFloat("Time",1.37f);
        for(int key=0;key<6;key++)
        {
            warp.SetInt("ColorKey",key);
            int captured=key;
            Test(warp,uv=>Reference.Warp(image,uv,0.28f,0.21f,1.37f,0.18f,captured),"Warp top-half center key="+key);
        }
        if(gl.GetError()!=GLEnum.NoError) throw new Exception("GL error after integrated FX tests");
        device.SetRenderTarget(0,null);
    }
}
