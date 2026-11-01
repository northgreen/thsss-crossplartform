using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using SkiaSharp;
using SlimDX.Direct3D9;
using Texture=SlimDX.Direct3D9.Texture;

internal static class GpuParityProbes
{
    internal static int Run(string temp)
    {
        var options=WindowOptions.Default;
        options.Size=new Silk.NET.Maths.Vector2D<int>(64,64); options.IsVisible=false;
        using var window=Window.Create(options); window.Initialize();
        using var gl=window.CreateOpenGL();
        using var device=new Device(gl,64,64);
        using var texture=new Texture(device,64,64,1,Usage.RenderTarget,Format.A8R8G8B8,Pool.Managed);
        using var surface=texture.GetSurfaceLevel(0);
        device.SetRenderTarget(0,surface);
        gl.Enable(Silk.NET.OpenGL.EnableCap.ScissorTest);
        gl.Scissor(0,0,64,32); gl.ClearColor(1,0,0,1); gl.Clear(ClearBufferMask.ColorBufferBit);
        gl.Scissor(0,32,64,32); gl.ClearColor(0,0,1,1); gl.Clear(ClearBufferMask.ColorBufferBit);
        gl.Disable(Silk.NET.OpenGL.EnableCap.ScissorTest);
        string file=Path.Combine(temp,"original-request-jpeg.jpg");
        Surface.ToFile(surface,file,ImageFileFormat.Jpg);
        byte[] bytes=File.ReadAllBytes(file);
        using var output=SKBitmap.Decode(file) ?? throw new InvalidDataException("Cannot decode screenshot: " + file);
        int count=0;
        if(bytes[0]==137&&bytes[1]==80&&bytes[2]==78&&bytes[3]==71)
        { count++; Console.WriteLine("CONFIRMED: in-game JPEG screenshot API writes PNG bytes under .jpg name"); }
        else Console.WriteLine("NOT REPRODUCED: screenshot file-type mismatch");
        if(output.GetPixel(4,4).Red>240&&output.GetPixel(4,60).Blue>240)
        { count++; Console.WriteLine("CONFIRMED: in-game screenshot rows upside down (screen top-blue becomes image bottom-blue)"); }
        else Console.WriteLine("NOT REPRODUCED: screenshot Y inversion");
        device.SetRenderTarget(0,null);
        return count;
    }
}
