using Silk.NET.OpenGL;
using SlimDX.Direct3D9;
using SkiaSharp;

internal static class BackbufferScaling
{
    internal static void Run(GL gl,string directory)
    {
        using var device=new Device(gl,128,64){GameWidth=128,GameHeight=64,FramebufferWidth=256,FramebufferHeight=192};
        uint draw=(uint)gl.GetInteger(GLEnum.DrawFramebufferBinding);
        bool scissor=gl.IsEnabled(Silk.NET.OpenGL.EnableCap.ScissorTest);
        try
        {
            gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer,0);
            gl.Disable(Silk.NET.OpenGL.EnableCap.ScissorTest);
            gl.ClearColor(0,1,0,1);gl.Clear(ClearBufferMask.ColorBufferBit);
            gl.Enable(Silk.NET.OpenGL.EnableCap.ScissorTest);
            // Logical 128x64 scaled to 256x128, vertically letterboxed in a 256x192 framebuffer.
            gl.Scissor(0,32,256,64);gl.ClearColor(1,0,0,1);gl.Clear(ClearBufferMask.ColorBufferBit);
            gl.Scissor(0,96,256,64);gl.ClearColor(0,0,1,1);gl.Clear(ClearBufferMask.ColorBufferBit);
            gl.Disable(Silk.NET.OpenGL.EnableCap.ScissorTest);
            using var back=device.GetBackBuffer(0,0);
            string path=Path.Combine(directory,"scaled-logical-backbuffer.png");
            Surface.ToFile(back,path,ImageFileFormat.Png);
            using var image=SKBitmap.Decode(path) ?? throw new InvalidDataException("Cannot decode screenshot: " + path);
            if(image.Width!=128||image.Height!=64||image.GetPixel(64,8).Blue<240||image.GetPixel(64,56).Red<240||image.GetPixel(64,8).Green>10)
                throw new Exception("Physical framebuffer screenshot must exclude letterbox and retain original logical dimensions/direction");
        }
        finally
        {
            if(scissor)gl.Enable(Silk.NET.OpenGL.EnableCap.ScissorTest);else gl.Disable(Silk.NET.OpenGL.EnableCap.ScissorTest);
            gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer,draw);
        }
        Console.WriteLine("PASS: scaled/letterboxed backbuffer saved at original logical resolution without border cropping");
    }
}
