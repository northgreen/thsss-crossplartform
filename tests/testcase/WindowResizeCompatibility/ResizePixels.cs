using Shooting;
using SlimDX;
using SlimDX.Direct3D9;
using THSSS.Graphics;
using SkiaSharp;

internal static class ResizePixels
{
    internal static void Check(Game_Main game, int width, int height)
    {
        var device = game.DeviceMain;
        var backend = device.RenderBackend;
        device.SetRenderTarget(0,null);
        backend.SetScissor(false,0,0,width,height);
        device.Clear(ClearFlags.Target | ClearFlags.ZBuffer,System.Drawing.Color.Black,1,0);
        using var image = new SKBitmap(64,64, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        for(int y=0;y<64;y++) for(int x=0;x<64;x++)
            image.SetPixel(x,y, y<32 ? (x<32 ? new(255,0,0,255) : new(0,255,0,255))
                : (x<32 ? new(0,0,255,255) : new(255,255,255,255)));
        using var texture = Texture.FromImage(device,image);
        using var sprite = new Sprite(device);
        device.SetSamplerState(0,SamplerState.MinFilter,TextureFilter.Point);
        device.SetSamplerState(0,SamplerState.MagFilter,TextureFilter.Point);
        sprite.Begin(SpriteFlags.AlphaBlend);
        sprite.Transform=Matrix.Scaling(10,7.5f,1);
        sprite.Draw(texture,null,null,Vector3.Zero,new Color4(1,1,1,1));
        sprite.End();
        var content=PresentationGeometry.Fit(width,height);
        var expected=new[]{new byte[]{0,0,255},new byte[]{255,255,255},new byte[]{255,0,0},new byte[]{0,255,0}};
        int i=0;
        foreach(int y in new[]{content.Y+content.Height/8,content.Y+content.Height*7/8})
        foreach(int x in new[]{content.X+content.Width/8,content.X+content.Width*7/8})
        {
            var p=backend.ReadPixels(x,y,1,1);
            for(int c=0;c<3;c++)if(Math.Abs(p[c]-expected[i][c])>10)
                throw new Exception($"Resized corner {i} is cropped/flipped: {string.Join(',',p)} at {width}x{height}");
            i++;
        }
        if(content.X>0) Black(backend.ReadPixels(content.X/2,height/2,1,1));
        if(content.Y>0) Black(backend.ReadPixels(width/2,content.Y/2,1,1));
        if(device.GL!=null && device.GL.GetError()!=Silk.NET.OpenGL.GLEnum.NoError)
            throw new Exception("GL error after offscreen/default/resize depth restoration.");
        Console.WriteLine($"PASS four corners and black borders {width}x{height} {backend.Kind}");
        device.SetSamplerState(0,SamplerState.MinFilter,TextureFilter.Linear);
        device.SetSamplerState(0,SamplerState.MagFilter,TextureFilter.Linear);
    }
    private static void Black(byte[] pixel)
    {
        if(pixel[0]>3||pixel[1]>3||pixel[2]>3)throw new Exception("Fullscreen border is not black.");
    }
}
