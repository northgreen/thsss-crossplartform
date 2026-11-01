using System.Reflection;
using System.Runtime.CompilerServices;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SlimDX.Direct3D9;
using Shooting;
using SkiaSharp;

internal static class ScreenshotTiming
{
    internal static void Run(GL gl,IWindow window,Device device,string directory)
    {
        string cwd=Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(directory);Directory.CreateDirectory("ScreenShot");
            var game=(Game_Main)RuntimeHelpers.GetUninitializedObject(typeof(Game_Main));
            var global=(GlobalDataPackage)RuntimeHelpers.GetUninitializedObject(typeof(GlobalDataPackage));
            global.DeviceMain=device;game.GlobalData=global;
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            gl.BindFramebuffer(FramebufferTarget.Framebuffer,0);
            gl.Disable(Silk.NET.OpenGL.EnableCap.ScissorTest);
            gl.ClearColor(0,1,0,1);gl.Clear(ClearBufferMask.ColorBufferBit);
            window.GLContext.SwapBuffers(); // Mirrors the previous frame being presented.
            typeof(Game_Main).GetMethod("ScreenShot",flags)!.Invoke(game,null);
            if(Directory.GetFiles("ScreenShot").Length!=0)throw new Exception("P captured an exchanged/undefined backbuffer before this frame rendered");
            gl.ClearColor(0,0,1,1);gl.Clear(ClearBufferMask.ColorBufferBit);
            typeof(Game_Main).GetMethod("CapturePendingScreenshot",flags)!.Invoke(game,null);
            typeof(Game_Main).GetMethod("CapturePendingScreenshot",flags)!.Invoke(game,null);
            var files=Directory.GetFiles("ScreenShot","*.jpg");
            if(files.Length!=1)throw new Exception("Screenshot request must execute exactly once");
            using var image=SKBitmap.Decode(files[0]) ?? throw new InvalidDataException("Cannot decode screenshot: " + files[0]);
            var pixel=image.GetPixel(image.Width/2,image.Height/2);
            if(pixel.Blue<230||pixel.Red>20||pixel.Green>20)throw new Exception("P screenshot did not capture the newly rendered blue frame after previous green swap");
        }
        finally { Directory.SetCurrentDirectory(cwd); }
        Console.WriteLine("PASS: actual P request after swap is deferred and captures newly rendered frame exactly once");
    }
}
