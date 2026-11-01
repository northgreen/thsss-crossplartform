using System.Reflection;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SkiaSharp;
using SlimDX;
using SlimDX.Direct3D9;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;
using Texture = SlimDX.Direct3D9.Texture;
using EnableCap = Silk.NET.OpenGL.EnableCap;

internal static class GpuChecks
{
    private const int Width = 96, Height = 80;
    internal static unsafe void Run()
    {
        var options = WindowOptions.Default;
        options.Size = new Silk.NET.Maths.Vector2D<int>(Width, Height);
        options.IsVisible = false;
        using var window = Window.Create(options); window.Initialize();
        using var gl = window.CreateOpenGL();
        var device = new Device(gl, Width, Height);
        Console.WriteLine($"GPU: {gl.GetStringS(StringName.Renderer)} / {gl.GetStringS(StringName.Version)}");
        using var image = new SKBitmap(17, 13, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
            image.SetPixel(x,y, new SKColor((byte)(95+x*7), (byte)(105+y*8), (byte)(100+(x+y)*4), (byte)(160+(x+y)*2)));
        using var texture = Texture.FromImage(device, image);
        uint target = gl.GenTexture(); gl.BindTexture(TextureTarget.Texture2D, target);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba32f, Width, Height, 0, Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.Float, null);
        uint fbo = gl.GenFramebuffer(); gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, target, 0);
        if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete) throw new Exception("Float framebuffer incomplete");
        uint outerSampler = gl.GenSampler();
        gl.BindSampler(0,outerSampler);
        uint vao = gl.GenVertexArray(), vbo = gl.GenBuffer();
        gl.BindVertexArray(vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        const uint stride = 9 * sizeof(float);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0); gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float)); gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, 5 * sizeof(float)); gl.EnableVertexAttribArray(2);
        gl.Viewport(0, 0, Width, Height); gl.Disable(EnableCap.Blend); gl.Disable(EnableCap.DepthTest); gl.Disable(EnableCap.CullFace);
        try
        {
            using var filter = Effect.FromFile(device, "../game/FX/Filter.fx");
            foreach (bool expanded in new[] { false, true })
                Compare("Filter mirror=" + expanded, Draw(filter, expanded), uv => Reference.Filter(image, uv), expanded);
            using var blur = Effect.FromFile(device, "../game/FX/Blur.fx");
            foreach (float level in new[] { 0f, 0.0025f, 0.009f })
            {
                blur.SetFloat("BlurLevel", level);
                foreach (bool expanded in new[] { false, true })
                    Compare($"Blur {level} mirror={expanded}", Draw(blur, expanded), uv => Reference.Blur(image, uv, level), expanded);
            }
            using var warp = Effect.FromFile(device, "../game/FX/Warp.fx");
            foreach (var scenario in new[] {
                (X: 29.5f/Width, Y: 23.5f/Height, Time: 0f, Radius: 0f),
                (X: 0.63f, Y: 0.31f, Time: 1.37f, Radius: 0.14f),
                (X: 0.025f, Y: 0.82f, Time: 4.2f, Radius: 0.19f) })
            {
                warp.SetFloat("X", scenario.X); warp.SetFloat("Y", scenario.Y);
                warp.SetFloat("Time", scenario.Time); warp.SetFloat("Radius", scenario.Radius);
                for (int key = -1; key <= 6; key++)
                {
                    warp.SetInt("ColorKey", key);
                    float[] actual = Draw(warp, false);
                    Compare($"Warp r={scenario.Radius} key={key}", actual,
                        uv => Reference.Warp(image, uv, scenario.X, scenario.Y, scenario.Time, scenario.Radius, key), false);
                    if (scenario.Radius == 0 && key == 0)
                    {
                        float[] baseline = Draw(filter, false);
                        int offset = (23*Width+29)*4;
                        float reduction = baseline[offset] - actual[offset];
                        if (MathF.Abs(reduction - 0.004f) > 0.00001f) throw new Exception($"Radius zero must still tint center: {reduction}");
                        Console.WriteLine($"PASS radius-zero center R subtraction={reduction:F6}");
                    }
                }
            }
            filter.SetTexture("xColoredTexture", texture);
            Compare("Explicit texture parameter", Draw(filter, false), uv => Reference.Filter(image, uv), false);
            if (gl.GetError() != GLEnum.NoError) throw new Exception("OpenGL error after effect tests");
        }
        finally
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            gl.BindSampler(0,0); gl.DeleteSampler(outerSampler);
            gl.DeleteFramebuffer(fbo); gl.DeleteTexture(target); gl.DeleteBuffer(vbo); gl.DeleteVertexArray(vao);
        }

        float[] Draw(Effect effect, bool expanded)
        {
            float lo = expanded ? -0.25f : 0, hi = expanded ? 1.25f : 1;
            float[] vertices = {
                -1,-1,0,lo,lo,0.1f,0.2f,0.3f,0.4f, 1,-1,0,hi,lo,0.1f,0.2f,0.3f,0.4f, -1,1,0,lo,hi,0.1f,0.2f,0.3f,0.4f,
                1,-1,0,hi,lo,0.1f,0.2f,0.3f,0.4f, 1,1,0,hi,hi,0.1f,0.2f,0.3f,0.4f, -1,1,0,lo,hi,0.1f,0.2f,0.3f,0.4f };
            gl.BindVertexArray(vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length*sizeof(float)), (ReadOnlySpan<float>)vertices, BufferUsageARB.DynamicDraw);
            gl.ActiveTexture(TextureUnit.Texture0); gl.BindTexture(TextureTarget.Texture2D, texture.GLName);
            effect.Begin(); effect.BeginPass(0);
            typeof(Effect).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(effect, new object[] { Matrix.Identity, Matrix.Identity });
            gl.DrawArrays(Silk.NET.OpenGL.PrimitiveType.Triangles, 0, 6);
            var pixels = new float[Width*Height*4];
            fixed (float* p = pixels) gl.ReadPixels(0,0,Width,Height,Silk.NET.OpenGL.PixelFormat.Rgba,PixelType.Float,p);
            typeof(Effect).GetMethod("Unbind", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(effect, null);
            if (gl.GetInteger(GLEnum.SamplerBinding) != outerSampler || gl.GetInteger(GLEnum.CurrentProgram) != 0) throw new Exception("Effect unbind leaked state");
            effect.EndPass(); effect.End();
            return pixels;
        }
    }
    private static void Compare(string label, float[] actual, Func<Vector2,Vector4> reference, bool expanded)
    {
        float maximum = 0;
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            var uv = new Vector2((x+0.5f)/Width, (y+0.5f)/Height);
            if (expanded) uv = uv*1.5f - new Vector2(0.25f);
            Vector4 expected = reference(uv);
            for (int channel = 0; channel < 4; channel++)
            {
                float error = MathF.Abs(actual[(y*Width+x)*4+channel] - expected[channel]);
                maximum = MathF.Max(maximum,error);
                if (!float.IsFinite(error) || error > 0.0025f) throw new Exception($"{label}: ({x},{y}) channel {channel}, expected {expected[channel]}, actual {actual[(y*Width+x)*4+channel]}, error={error}");
            }
        }
        Console.WriteLine($"PASS {label}: {Width*Height} pixels / all RGBA, max error={maximum:F7}");
    }
}
