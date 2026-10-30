using System.Reflection;
using SlimDX;
using SlimDX.Direct3D9;
using THSSS.Graphics;
using THSSS.Graphics.Vulkan;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

internal static class SpriteChecks
{
    internal static void Run(VulkanRenderBackend backend)
    {
        using var device = new Device { GameWidth = 8, GameHeight = 8, FramebufferWidth = 8, FramebufferHeight = 8 };
        typeof(Device).GetField("_renderBackend", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(device, backend);
        using var output = backend.CreateRenderTexture(8, 8, TextureFormat.Rgba8);
        using var framebuffer = backend.CreateFramebuffer(output, DepthFormat.None, 8, 8);
        backend.BindFramebuffer(framebuffer);
        backend.Clear(ClearMask.Color, 0, 0, 1, 1, 1, 0);
        using var image = new Image<Rgba32>(8, 8, new Rgba32(255, 0, 0, 255));
        using var texture = Texture.FromImage(device, image);
        using var sprite = new Sprite(device);
        sprite.Begin(SpriteFlags.AlphaBlend);
        sprite.Draw(texture, Vector3.Zero, Vector3.Zero, new Color4(1, 1, 1, 1));
        sprite.End();
        var pixel = backend.ReadPixels(4, 4, 1, 1);
        if (pixel[0] < 240 || pixel[1] > 15 || pixel[2] > 15)
            throw new Exception("Actual Sprite shader + uploaded red image must draw red, not constant-blue clear: " + string.Join(',', pixel));
        Console.WriteLine("PASS: actual Sprite/UBO/texture/batch pipeline renders uploaded red image");
        using var transparentImage = new Image<Rgba32>(8, 8, new Rgba32(0, 255, 0, 0));
        using var transparent = Texture.FromImage(device, transparentImage);
        sprite.Begin(SpriteFlags.AlphaBlend);
        sprite.Draw(transparent, Vector3.Zero, Vector3.Zero, new Color4(1, 1, 1, 1));
        sprite.End();
        var blended = backend.ReadPixels(4, 4, 1, 1);
        if (blended[0] < 240 || blended[1] > 15)
            throw new Exception("Transparent Sprite must preserve opaque background (Vulkan SetRenderState must not be skipped): " + string.Join(',', blended));
        Console.WriteLine("PASS: Vulkan Sprite alpha blend preserves background under transparent overlay");
        using var filterImage = new Image<Rgba32>(2,1);
        filterImage[0,0] = new Rgba32(255,0,0,255);
        filterImage[1,0] = new Rgba32(0,0,255,255);
        using var filterTexture = Texture.FromImage(device,filterImage);
        device.SetSamplerState(0,SamplerState.MinFilter,TextureFilter.Point);
        device.SetSamplerState(0,SamplerState.MagFilter,TextureFilter.Point);
        device.ApplySamplerState();
        sprite.Transform = Matrix.Scaling(4f,8f,1f);
        sprite.Begin(SpriteFlags.AlphaBlend);
        sprite.Draw(filterTexture,Vector3.Zero,Vector3.Zero,Color4.White);
        sprite.End();
        var filtered = backend.ReadPixels(3,4,1,1);
        if (filtered[0] < 40 || filtered[0] > 220 || filtered[2] < 40 || filtered[2] > 220)
            throw new Exception("Sprite must replace inherited point sampling with linear interpolation: " + string.Join(',',filtered));
        sprite.Begin(SpriteFlags.AlphaBlend | SpriteFlags.DoNotModifyRenderState);
        sprite.Draw(filterTexture,Vector3.Zero,Vector3.Zero,Color4.White);
        sprite.End();
        var restored = backend.ReadPixels(3,4,1,1);
        if (restored[0] < 240 || restored[2] > 15)
            throw new Exception("Sprite.End must restore point sampling and DoNotModifyRenderState must preserve it: " + string.Join(',',restored));
        sprite.Transform = Matrix.Identity;
        Console.WriteLine("PASS: Sprite linear sampler defaults, state restore and DoNotModifyRenderState");
        device.GameWidth = 640; device.GameHeight = 480;
        device.FramebufferWidth = 640; device.FramebufferHeight = 480;
        backend.BindFramebuffer(null);
        backend.Clear(ClearMask.Color, 0, 0, 0, 1, 1, 0);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "game/Image/Interface/MenuBG.png"))) root = root.Parent;
        using var menu = Texture.FromFile(device, Path.Combine(root!.FullName, "game/Image/Interface/MenuBG.png"));
        var drawing = new Shooting.MySprite(device);
        drawing.Begin(SpriteFlags.AlphaBlend);
        drawing.Draw2D(menu, new System.Drawing.Rectangle(0, 0, 1024, 768),
            new System.Drawing.SizeF(640, 480), new System.Drawing.PointF(0, 0), System.Drawing.Color.White);
        drawing.End();
        var centre = backend.ReadPixels(320, 240, 1, 1);
        if (centre[0] < 150 || centre[1] < 150 || centre[2] < 200)
            throw new Exception("Actual scaled MenuBG atlas must retain its light centre: " + string.Join(',', centre));
        Console.WriteLine("PASS: actual menu atlas through MySprite scaling: " + string.Join(',', centre));
        drawing.Dispose();
        backend.BindFramebuffer(framebuffer);
        device.GameWidth = 8; device.GameHeight = 8; device.FramebufferWidth = 8; device.FramebufferHeight = 8;
        backend.BeginFrame();
        backend.Clear(ClearMask.Color, 0, 0, 1, 1, 1, 0);
        for (int i = 0; i < 400; i++)
        {
            sprite.Begin(SpriteFlags.AlphaBlend);
            sprite.Draw(i % 2 == 0 ? texture : transparent, Vector3.Zero, Vector3.Zero, new Color4(1, 1, 1, 1));
            sprite.End();
        }
        backend.Present();
        var batched = backend.ReadPixels(4, 4, 1, 1);
        if (batched[0] < 240 || batched[1] > 15 || backend.FrameSubmissions > 2 || backend.FrameDraws != 400)
            throw new Exception($"400 batched sprites must preserve per-draw data and use <=2 submits: pixel={string.Join(',',batched)} submits={backend.FrameSubmissions} draws={backend.FrameDraws}");
        Console.WriteLine($"PASS: 400 real Sprite batches, submits={backend.FrameSubmissions}, independent vertex/uniform/descriptor snapshots");
        backend.BeginFrame();
        backend.Clear(ClearMask.Color, 0, 0, 1, 1, 1, 0);
        for (int i = 0; i < 8; i++)
        {
            sprite.Begin(SpriteFlags.AlphaBlend);
            sprite.Transform = Matrix.Translation(i, 0, 0);
            sprite.Draw(texture, new System.Drawing.Rectangle(0, 0, 1, 8), Vector3.Zero,
                i % 2 == 0 ? new Color4(1, 1, 1, 1) : new Color4(0, 1, 1, 1));
            sprite.End();
        }
        backend.Present();
        var stripes = backend.ReadPixels(0, 4, 8, 1);
        for (int i = 0; i < 8; i++)
            if (Math.Abs(stripes[i * 4] - (i % 2 == 0 ? 255 : 0)) > 5 || stripes[i * 4 + 2] > 5)
                throw new Exception("Batched draws lost their independent transform/tint at column " + i);
        sprite.Transform = Matrix.Identity;
        Console.WriteLine("PASS: alternating transformed/tinted batched strips retain each draw's data");
        backend.BeginFrame();
        backend.EndFrame();
        backend.BeginFrame();
        backend.Clear(ClearMask.Color, 0, 0, 1, 1, 1, 0);
        backend.EndFrame();
        backend.EndFrame();
        backend.BeginFrame(); backend.Present();
        var skipped = backend.ReadPixels(4, 4, 1, 1);
        if (skipped[2] != 255) throw new Exception("A skipped presentation left the frame open or discarded recorded work.");
        Console.WriteLine("PASS: empty/skipped presentation frames finish safely and EndFrame is idempotent");
        using var font = new SlimDX.Direct3D9.Font(device, new System.Drawing.Font("Arial", 8));
        font.DrawString(sprite, "cached", 0, 0, System.Drawing.Color.White);
        var cachedField = typeof(SlimDX.Direct3D9.Font).GetField("_cachedGlyphs", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var glyphs = cachedField.GetValue(font);
        font.DrawString(sprite, "cached", 1, 0, System.Drawing.Color.White);
        if (!ReferenceEquals(glyphs, cachedField.GetValue(font))) throw new Exception("Repeated text at a new position should reuse glyph texture.");
        font.DrawString(sprite, "changed", 1, 0, System.Drawing.Color.Red);
        if (ReferenceEquals(glyphs, cachedField.GetValue(font))) throw new Exception("Changed text/style must invalidate glyph cache.");
        Console.WriteLine("PASS: bounded glyph cache reuses static text and invalidates changed text/style");
        backend.BindFramebuffer(null);
    }
}
