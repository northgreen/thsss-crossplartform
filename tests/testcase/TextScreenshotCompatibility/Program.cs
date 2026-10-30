using System.Reflection;
using System.Runtime.CompilerServices;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SlimDX;
using SlimDX.Direct3D9;
using Shooting;
// Several types collide by name across Silk.NET, System.Drawing and the SlimDX compat
// layer; these aliases pin each one to the exact type the game code uses.
using EnableCap = Silk.NET.OpenGL.EnableCap;
using Color = System.Drawing.Color;
using Rectangle = System.Drawing.Rectangle;
using DXFont = SlimDX.Direct3D9.Font;
using Texture = SlimDX.Direct3D9.Texture;

// Two jobs in one fixture. First: font measurement and drawing must match the original GDI+
// behaviour (point vs pixel sizes, Chinese metrics, wrapping, clipping). Second: in-game
// screenshots must capture the frame that was just drawn, before the back buffer is
// presented, and must leave the GL state they touched exactly as they found it.
// Every assertion is read back from the GPU, not inferred from the code path.
int assertions = 0;
void Check(bool condition, string description)
{
    assertions++;
    if (!condition) throw new Exception(description);
}

// Reproduce deferred loading: a face can have readable name metadata but lack
// a required outline table. Use an installed TTF only as a temporary fixture.
byte[] missingLocaFont = null;
foreach (var family in SixLabors.Fonts.SystemFonts.Families)
{
    var probe = family.CreateFont(12);
    if (!probe.TryGetPath(out string sourcePath) || !sourcePath.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)) continue;
    byte[] bytes = File.ReadAllBytes(sourcePath);
    if (bytes.Length < 12 || System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes) != 0x00010000) continue;
    int tables = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
    for (int table = 0; table < tables; table++)
    {
        int offset = 12 + table * 16;
        if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset)) != 0x6c6f6361) continue;
        bytes[offset] = (byte)'x';
        missingLocaFont = bytes;
        break;
    }
    if (missingLocaFont != null) break;
}
Check(missingLocaFont != null, "Font regression fixture requires an installed TrueType face with a loca table");
string brokenFontPath = Path.Combine(Path.GetTempPath(), "thsss-font-" + Guid.NewGuid().ToString("N") + ".ttf");
var originalError = Console.Error;
try
{
    File.WriteAllBytes(brokenFontPath, missingLocaFont);
    var collection = new SixLabors.Fonts.FontCollection();
    var brokenFamily = collection.Add(brokenFontPath);
    var validate = typeof(System.Drawing.Graphics).GetMethod("TryCreateReadableFont", BindingFlags.Static | BindingFlags.NonPublic)!;
    object[] parameters = { brokenFamily, 12f, SixLabors.Fonts.FontStyle.Regular, null };
    using var diagnostics = new StringWriter();
    Console.SetError(diagnostics);
    Check(!(bool)validate.Invoke(null, parameters)! && parameters[3] == null,
        "A name-only face with missing loca is rejected before text layout");
    string firstDiagnostic = diagnostics.ToString();
    Check(firstDiagnostic.Contains(brokenFontPath) && firstDiagnostic.Contains("MissingFontTableException") && firstDiagnostic.Contains("loca"),
        "Rejected font retains path and complete parser error");
    Check(!(bool)validate.Invoke(null, parameters)! && diagnostics.ToString() == firstDiagnostic,
        "Rejected face is cached without repeated parsing or repeated errors");
}
finally
{
    Console.SetError(originalError);
    if (File.Exists(brokenFontPath)) File.Delete(brokenFontPath);
}

var options = WindowOptions.Default;
options.Size = new Silk.NET.Maths.Vector2D<int>(256, 192);
options.IsVisible = false;
using var window = Window.Create(options);
window.Initialize();
using var gl = window.CreateOpenGL();
Check(window.FramebufferSize.X == 256 && window.FramebufferSize.Y == 192,
    $"Expected test framebuffer size 256x192, got {window.FramebufferSize}");
using var device = new Device(gl, 256, 192) { GameWidth = 256, GameHeight = 192 };
// GDI+ measures point-sized fonts; the original dialog built fonts as points, so the
// compatibility renderer must keep that unit rather than silently assuming pixels.
using var drawingFont = new System.Drawing.Font("楷体", 16, System.Drawing.FontStyle.Bold);
Check(drawingFont.Unit == System.Drawing.GraphicsUnit.Point, "Default styled font uses points, as GDI+ does");
using var defaultFont = new System.Drawing.Font("楷体", 16);
Check(defaultFont.Unit == System.Drawing.GraphicsUnit.Point, "Default regular font uses points");
using var font = new DXFont(device, drawingFont);
// 16 pt at the renderer's fixed 96 DPI equals 21.33 px; the two constructions must be
// interchangeable or every dialogue line would be laid out at the wrong size.
using var equivalentPixels = new DXFont(device, new System.Drawing.Font("楷体", 16f * 96f / 72f,
    System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel));
Check(font.MeasureString("夏夜对白") == equivalentPixels.MeasureString("夏夜对白"), "16pt equals 21.33px at the renderer's 96 DPI");
// A full dialogue-length CJK run: the widths must come from real glyph advances, not from a
// per-character constant, which is the classic way to get this wrong in a port.
string dialogue = string.Concat(Enumerable.Repeat("夏夜的风吹过森林，这是一段很长的中文剧情对白。", 3));
var measured = font.MeasureString(dialogue);
Check(measured.Width > 600 && measured.Height > 5, $"Chinese width/height: {measured}");
Check(font.MeasureString("") == System.Drawing.Size.Empty, "Empty text size");
Check(font.MeasureString((string)null) == System.Drawing.Size.Empty, "Null text size");
// Newlines add a line unless SingleLine asks for them to be treated as spaces.
Check(font.MeasureString("夏夜\r\n对白").Height > font.MeasureString("夏夜对白").Height, "Multiline height");
Check(font.MeasureString("夏夜\r\n对白", DrawTextFormat.SingleLine).Height == font.MeasureString("夏夜 对白").Height, "SingleLine newline normalization");
// WordBreak must fit the given width and grow in height; the bounded overload has to agree
// with the format-flag one, since the game uses both spellings.
var wrapped = font.MeasureString(null, dialogue, DrawTextFormat.WordBreak, 180);
Check(wrapped.Width <= 181 && wrapped.Height > measured.Height * 2, $"WordBreak width and height: {wrapped}");
Check(font.MeasureString(dialogue, 180, 2000) == wrapped, "Bounded overload uses same layout");
// Size really has to drive the metrics, not just the texture.
using (var larger = new DXFont(device, new System.Drawing.Font("楷体", 32, System.Drawing.FontStyle.Bold)))
    Check(larger.MeasureString("夏夜对白").Width > font.MeasureString("夏夜对白").Width * 1.8, "Font size affects measurement");
// The native D3D9 constructor takes a logical height in pixels; building the same text from
// a pixel-sized System.Drawing.Font must agree with it and be smaller than the point one.
using (var native = new DXFont(device, 16, 0, FontWeight.Bold, 1, false, CharacterSet.Default,
    Precision.Default, FontQuality.Default, FontPitch.Default, "楷体"))
using (var explicitPixels = new DXFont(device, new System.Drawing.Font("楷体", 16,
    System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel)))
{
    Check(native.MeasureString("夏夜对白") == explicitPixels.MeasureString("夏夜对白"), "Native height remains pixels");
    Check(font.MeasureString("夏夜对白").Width > explicitPixels.MeasureString("夏夜对白").Width, "Default point size exceeds same numeric pixel size");
}

// Drawing targets an offscreen 256x192 render target so ink can be measured pixel by pixel.
using var target = new Texture(device, 256, 192, 1, Usage.RenderTarget, Format.A8R8G8B8, Pool.Default);
using var surface = target.GetSurfaceLevel(0);
device.SetRenderTarget(0, surface);
using var sprite = new Sprite(device);
byte[] ReadPixels()
{
    var pixels = new byte[256 * 192 * 4];
    unsafe { fixed (byte* p = pixels) gl.ReadPixels(0, 0, 256, 192, Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, p); }
    return pixels;
}
// Ink = any pixel that is brighter than the black clear colour. GL rows arrive bottom-up,
// so y is flipped when scanning to keep the reported bounds in top-left screen space.
(int Count, int Left, int Top, int Right, int Bottom) InkBounds(byte[] pixels)
{
    int count = 0, left = 256, right = -1, top = 192, bottom = -1;
    for (int y = 0; y < 192; y++)
    for (int x = 0; x < 256; x++)
    {
        int i = ((191 - y) * 256 + x) * 4;
        if (pixels[i] < 20 && pixels[i + 1] < 20 && pixels[i + 2] < 20) continue;
        count++; left = Math.Min(left, x); right = Math.Max(right, x);
        top = Math.Min(top, y); bottom = Math.Max(bottom, y);
    }
    return (count, left, top, right, bottom);
}
void Clear() => device.Clear(ClearFlags.Target, Color.Black, 1, 0);
// A sprite batch must be presentable before End(): glyphs are drawn immediately, so a read
// taken between DrawString and sprite.End() still has to contain the text.
Clear();
sprite.Begin(SpriteFlags.AlphaBlend);
font.DrawString(sprite, "夏夜对白", 12, 17, Color.White);
var beforeEnd = InkBounds(ReadPixels());
Check(beforeEnd.Count > 150, $"Font pixels must exist before temporary texture disposal/End: {beforeEnd}");
font.DrawString(sprite, "ver 1.00", 110, 60, new SlimDX.Direct3D9.Color4(1f, 0f, 0f, 1f));
sprite.End();
var rendered = ReadPixels();
Check(InkBounds(rendered).Count > beforeEnd.Count, "Color4 draw overload adds pixels");
// The origin is honoured: ink starts at the requested (12,17) and stays left of the second run.
Check(beforeEnd.Left >= 12 && beforeEnd.Top >= 17 && beforeEnd.Right < 110, $"XY origin: {beforeEnd}");
// Cross-check against GDI+ rasterising the same string on the CPU: coverage may differ
// slightly (hinting), but a wildly different count means the glyph pipeline is wrong.
using (var bitmap = new System.Drawing.Bitmap(256, 192))
{
    using var graphics = System.Drawing.Graphics.FromImage(bitmap);
    graphics.DrawString("夏夜对白", drawingFont, System.Drawing.Brushes.White, 12, 17);
    var data = bitmap.LockBits(new Rectangle(0, 0, 256, 192), System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);
    var cpu = new byte[256 * 192 * 4];
    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, cpu, 0, cpu.Length);
    bitmap.UnlockBits(data);
    int cpuCount = 0;
    for (int i = 3; i < cpu.Length; i += 4) if (cpu[i] > 20) cpuCount++;
    Check(cpuCount > 150 && beforeEnd.Count > 150 && Math.Abs(cpuCount - beforeEnd.Count) <= Math.Max(cpuCount, beforeEnd.Count) / 10, $"Graphics and DXFont raster coverage approximately agree: CPU={cpuCount}, GPU={beforeEnd.Count}");
}
// DrawText alignment flags must place the ink inside the rectangle on the correct side.
foreach (var flags in new[] { DrawTextFormat.Left, DrawTextFormat.Center | DrawTextFormat.VCenter,
    DrawTextFormat.Right | DrawTextFormat.Bottom })
{
    Clear();
    sprite.Begin(SpriteFlags.AlphaBlend);
    font.DrawText(sprite, "夏夜", new Rectangle(20, 20, 180, 120), flags, Color.White);
    sprite.End();
    var ink = InkBounds(ReadPixels());
    Check(ink.Count > 80 && ink.Left >= 20 && ink.Right < 200 && ink.Top >= 20 && ink.Bottom < 140, $"Rectangle clipping {flags}: {ink}");
    if ((flags & DrawTextFormat.Center) != 0) Check(ink.Left > 70 && ink.Top > 60, $"Center/VCenter placement: {ink}");
    if ((flags & DrawTextFormat.Right) != 0) Check(ink.Left > 150 && ink.Top > 110, $"Right/Bottom placement: {ink}");
}
// A null sprite means "use the renderer's own batch", so the text must still appear.
Clear();
font.DrawText(null, "夏夜", 8, 8, Color.White);
Check(InkBounds(ReadPixels()).Count > 80, "Null sprite owns a rendering batch");
// WordBreak inside a small rectangle: text stays inside the clip box.
Clear();
sprite.Begin(SpriteFlags.AlphaBlend);
font.DrawText(sprite, dialogue, new Rectangle(10, 10, 90, 50), DrawTextFormat.WordBreak, Color.White);
sprite.End();
var clipped = InkBounds(ReadPixels());
Check(clipped.Count > 100 && clipped.Right < 100 && clipped.Bottom < 60, $"WordBreak clipping: {clipped}");
// NoClip is the opposite contract: drawing runs past the rectangle.
Clear();
sprite.Begin(SpriteFlags.AlphaBlend);
font.DrawText(sprite, "夏夜对白", new Rectangle(10, 10, 15, 10), DrawTextFormat.NoClip, Color.White);
sprite.End();
Check(InkBounds(ReadPixels()).Right > 25, "NoClip draws beyond rectangle");

// The real StoryBox is exercised through reflection so the production line-breaking logic is
// what gets validated, not a reimplementation of it. Only the fields it touches are filled.
var global = (GlobalDataPackage)RuntimeHelpers.GetUninitializedObject(typeof(GlobalDataPackage));
global.DeviceMain = device;
global.FontType = "楷体";
global.SpriteMain = new MySprite(device);
global.TextureObjectDictionary = new() { ["对话框"] = new TextureObject { PosRect = new Rectangle(0, 0, 220, 100) } };
var stage = (StageDataPackage)RuntimeHelpers.GetUninitializedObject(typeof(StageDataPackage));
stage.GlobalData = global;
stage.BoundRect = new Rectangle(0, 0, 256, 192);
var storyType = typeof(BaseObject).Assembly.GetType("Shooting.StoryBox", true)!;
var story = (BaseObject)Activator.CreateInstance(storyType, stage)!;
storyType.GetProperty("Text")!.SetValue(story, dialogue);
storyType.GetMethod("DrawFullText")!.Invoke(story, null);
string fullText = (string)storyType.GetProperty("DrawText")!.GetValue(story)!;
// Wrapping may only insert breaks: removing them must reproduce the input exactly, otherwise
// characters were dropped or mangled.
Check(fullText.Contains("\r\n") && fullText.Replace("\r\n", "") == dialogue, "Actual StoryBox.DrawFullText inserts newlines without losing Chinese characters");
// The original dialogue box tolerates 200 px per line after its 1.15 correction factor.
Check(fullText.Split("\r\n").All(line => font.MeasureString(line).Width / 1.15f <= 200), "Actual StoryBox lines respect dialogue width threshold");
// StoryBox owns GPU resources; release them or the process keeps the device alive.
foreach (string field in new[] { "StringTexture", "StringTexture2", "sysfont" })
    (storyType.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(story) as IDisposable)?.Dispose();
(storyType.GetProperty("DXfont", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(story) as IDisposable)?.Dispose();
global.SpriteMain.sprite.Dispose();
Console.WriteLine($"PASS text: Chinese measurement={measured}, wrapped={wrapped}, actual StoryBox lines={fullText.Split("\r\n").Length}, direct GPU ink={beforeEnd.Count}");

// Screenshot half. Every surface is painted with asymmetric bands (blue top, red bottom) so a
// vertically flipped or wrong-source capture cannot pass.
string temp = Path.Combine(Path.GetTempPath(), "thsss-text-screenshot-" + Guid.NewGuid());
Directory.CreateDirectory(temp);
void PaintBands(uint fbo)
{
    gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
    gl.Disable(EnableCap.ScissorTest);
    gl.ClearColor(1, 0, 0, 1); gl.Clear(ClearBufferMask.ColorBufferBit);
    gl.Enable(EnableCap.ScissorTest);
    gl.Scissor(0, 72, 256, 120);
    gl.ClearColor(0, 0, 1, 1); gl.Clear(ClearBufferMask.ColorBufferBit);
    gl.Disable(EnableCap.ScissorTest);
}
// The CPU-side reference image uses top-down rows: y < 120 is blue, matching the GL scissor
// band only after the flip a screenshot must apply.
using var sourceImage = new Image<Rgba32>(256, 192);
for (int y = 0; y < 192; y++)
for (int x = 0; x < 256; x++)
    sourceImage[x, y] = y < 120 ? new Rgba32(0, 0, 255, 255) : new Rgba32(255, 0, 0, 255);
using var plainTexture = Texture.FromImage(device, sourceImage);
using var plainSurface = plainTexture.GetSurfaceLevel(0);
// A third FBO that is green at capture time: the screenshot must read the requested source,
// never whatever happens to be bound.
using var other = new Surface(gl, 256, 192, true);
using var backbuffer = device.GetBackBuffer(0, 0);
// A pixel-pack buffer plus a non-default pack layout: the caption path must restore both.
uint pack = gl.GenBuffer();
try
{
    PaintBands(surface.FBO);
    PaintBands(0);
    // Before any capture, the default read buffer must follow the double-buffered window: the
    // back buffer while drawing, the front buffer for a single-buffered context.
    Check(gl.GetInteger(GLEnum.ReadBuffer) == (int)(gl.GetInteger(GLEnum.Doublebuffer) != 0 ? ReadBufferMode.Back : ReadBufferMode.Front),
        $"Default read buffer={gl.GetInteger(GLEnum.ReadBuffer)}, draw={gl.GetInteger(GLEnum.DrawBuffer)}, double={gl.GetInteger(GLEnum.Doublebuffer)}");
    var defaultPixels = ReadPixels();
    int topOffset = ((191 - 20) * 256 + 128) * 4;
    Check(defaultPixels[topOffset + 2] > 230,
        $"Default framebuffer painted blue: {defaultPixels[topOffset]},{defaultPixels[topOffset+1]},{defaultPixels[topOffset+2]}; draw buffer={gl.GetInteger(GLEnum.DrawBuffer)}, read buffer={gl.GetInteger(GLEnum.ReadBuffer)}, double={gl.GetInteger(GLEnum.Doublebuffer)}");
    // Setup for the state-preservation test: split draw/read bindings, an explicit read buffer,
    // a bound pack buffer and a custom pack layout that the capture code must not leave behind.
    gl.BindFramebuffer(FramebufferTarget.Framebuffer, other.FBO);
    gl.ClearColor(0, 1, 0, 1); gl.Clear(ClearBufferMask.ColorBufferBit);
    gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, other.FBO);
    gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, other.FBO);
    gl.ReadBuffer(ReadBufferMode.ColorAttachment0);
    gl.BindBuffer(BufferTargetARB.PixelPackBuffer, pack);
    gl.PixelStore(GLEnum.PackAlignment, 8);
    gl.PixelStore(GLEnum.PackRowLength, 300);
    gl.PixelStore(GLEnum.PackSkipRows, 2);
    gl.PixelStore(GLEnum.PackSkipPixels, 3);
    void CheckState()
    {
        Check(gl.GetInteger(GLEnum.DrawFramebufferBinding) == other.FBO, "Draw FBO restored");
        Check(gl.GetInteger(GLEnum.ReadFramebufferBinding) == other.FBO, "Read FBO restored");
        Check(gl.GetInteger(GLEnum.ReadBuffer) == (int)ReadBufferMode.ColorAttachment0, "Read buffer restored");
        Check(gl.GetInteger(GLEnum.PixelPackBufferBinding) == pack, "Pack buffer restored");
        Check(gl.GetInteger(GLEnum.PackAlignment) == 8 && gl.GetInteger(GLEnum.PackRowLength) == 300 &&
            gl.GetInteger(GLEnum.PackSkipRows) == 2 && gl.GetInteger(GLEnum.PackSkipPixels) == 3, "Pack layout restored");
    }
    // Every source x every supported format: the file extension is deliberately wrong so the
    // encoder choice has to come from the format argument, not from the file name.
    foreach (var source in new[] { surface, backbuffer, plainSurface })
    foreach (var format in new[] { ImageFileFormat.Jpg, ImageFileFormat.Png, ImageFileFormat.Bmp, ImageFileFormat.Tga, ImageFileFormat.Ppm })
    {
        string file = Path.Combine(temp, $"{source.FBO}-{format}.wrong-extension");
        Surface.ToFile(source, file, format);
        CheckState();
        var bytes = File.ReadAllBytes(file);
        if (format == ImageFileFormat.Jpg) Check(bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff, "JPEG magic, not PNG at .jpg");
        if (format == ImageFileFormat.Png) Check(bytes.Take(8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}), "PNG magic");
        if (format == ImageFileFormat.Ppm) Check(bytes[0] == 'P' && (bytes[1] == '6' || bytes[1] == '3'), "PPM RGB magic");
        using var saved = SixLabors.ImageSharp.Image.Load<Rgba32>(file);
        var top = saved[128, 20]; var bottom = saved[128, 175];
        Check(saved.Width == 256 && saved.Height == 192, "Screenshot dimensions");
        // Orientation check: blue must be at the top row and red at the bottom, matching what
        // the player saw, not the flipped GL memory layout.
        Check(top.B > 230 && top.R < 20 && top.G < 20, $"{source.FBO}/{format} source/top blue: {top}");
        Check(bottom.R > 230 && bottom.B < 20 && bottom.G < 20, $"{source.FBO}/{format} source/bottom red: {bottom}");
    }
    // An unsupported container must fail explicitly and leave no mislabelled file behind.
    try { Surface.ToFile(surface, Path.Combine(temp, "unsupported.dds"), ImageFileFormat.Dds); throw new Exception("DDS must reject explicitly"); }
    catch (NotSupportedException) { Check(!File.Exists(Path.Combine(temp, "unsupported.dds")), "Unsupported format does not create mislabeled output"); }
    CheckState();
    // An I/O failure must surface and must not leave the GL state half-restored.
    try { Surface.ToFile(surface, Path.Combine(temp, "missing", "failure.jpg"), ImageFileFormat.Jpg); throw new Exception("Missing output directory must fail"); }
    catch (DirectoryNotFoundException) { CheckState(); }
    Check(gl.GetError() == GLEnum.NoError, "Screenshot GL calls have no errors");
    // Scaling from an arbitrary backbuffer size and the in-game capture timing are covered by
    // their own helpers.
BackbufferScaling.Run(gl,temp);
ScreenshotTiming.Run(gl,window,device,temp);
    Console.WriteLine("PASS screenshots: source RT and default backbuffer independent of current green FBO; JPEG/PNG/BMP/TGA/PPM; asymmetric blue-top/red-bottom; restored split bindings, read buffer, PBO and pack layout, including IO failure");
}
finally
{
    // Undo the fixture's own GL state so nothing it set leaks into later code.
    gl.BindBuffer(BufferTargetARB.PixelPackBuffer, 0);
    gl.DeleteBuffer(pack);
    gl.PixelStore(GLEnum.PackAlignment, 4);
    gl.PixelStore(GLEnum.PackRowLength, 0);
    gl.PixelStore(GLEnum.PackSkipRows, 0);
    gl.PixelStore(GLEnum.PackSkipPixels, 0);
    device.SetRenderTarget(0, null);
    Directory.Delete(temp, true);
}
Console.WriteLine($"PASS TextScreenshotCompatibility: {assertions} strict assertions");
