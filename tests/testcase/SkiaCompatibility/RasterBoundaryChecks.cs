using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;
using SlimDX.Direct3D9;
using THSSS.Graphics;
using THSSS.Imaging;

internal static class RasterBoundaryChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string description) { if (!value) throw new Exception(description); checks++; }
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
            start.ArgumentList.Add(typeof(RasterBoundaryChecks).Assembly.Location);
        start.ArgumentList.Add("missing-native-child");
        using (var child = Process.Start(start) ?? throw new Exception("Cannot launch native-failure probe"))
        {
            var output = child.StandardOutput.ReadToEndAsync();
            var errors = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(30_000)) { child.Kill(true); throw new TimeoutException("Native-failure probe timed out"); }
            Task.WaitAll(output, errors);
            Check(child.ExitCode == 0 && output.Result.Contains("PASS missing-native:"), "Missing native library was hidden: " + errors.Result);
        }
        using (var stream = new MemoryStream(new byte[] { 0, 1, 2, 3 }))
        {
            try { using var bad = RasterCodec.Decode(stream); throw new Exception("Malformed data accepted"); }
            catch (InvalidDataException) { }
            Check(stream.CanRead, "Decode failure closed the caller stream");
        }
        using (var stream = new MemoryStream(RasterFixtures.Png()))
        {
            using var image = RasterCodec.Decode(stream);
            Check(stream.CanRead, "Decode success closed the caller stream");
        }
        byte[] padded = { 255, 0, 0, 255, 0, 255, 0, 128, 91, 92, 93, 94,
            0, 0, 255, 255, 255, 255, 255, 0, 95, 96, 97, 98 };
        var pin = GCHandle.Alloc(padded, GCHandleType.Pinned);
        try
        {
            using var bitmap = new SKBitmap();
            Check(bitmap.InstallPixels(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Unpremul), pin.AddrOfPinnedObject(), 12), "Install padded fixture");
            byte[] expected = padded[..8].Concat(padded[12..20]).ToArray();
            Check(RasterCodec.CopyPixels(bitmap).SequenceEqual(expected), "Padded rows leaked padding or lost hidden transparent RGB");
        }
        finally { pin.Free(); }
        using (var bitmap = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul)))
        {
            Marshal.Copy(new byte[] { 64, 32, 16, 128 }, 0, bitmap.GetPixels(), 4);
            byte[] straight = RasterCodec.CopyPixels(bitmap);
            Check(Math.Abs(straight[0] - 128) <= 1 && Math.Abs(straight[1] - 64) <= 1 && Math.Abs(straight[2] - 32) <= 1 && straight[3] == 128, "Premultiplied color was uploaded as straight RGB");
        }
        using (var linear = SKColorSpace.CreateSrgbLinear())
        using (var bitmap = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul, linear)))
        using (var stream = new MemoryStream())
        {
            Marshal.Copy(new byte[] { 128, 64, 192, 255 }, 0, bitmap.GetPixels(), 4);
            RasterCodec.Encode(bitmap, stream, RasterFormat.Png);
            stream.Position = 0;
            using var decoded = RasterCodec.Decode(stream);
            Check(RasterCodec.CopyPixels(decoded).SequenceEqual(new byte[] { 128, 64, 192, 255 }), "Tagged PNG silently changed stored colors at the GPU upload boundary");
        }
        using var device = new Device();
        var backend = new FailingUploadBackend();
        typeof(Device).GetField("_renderBackend", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(device, backend);
        using (var empty = new SKBitmap())
        {
            try { using var texture = Texture.FromImage(device, empty); throw new Exception("Empty bitmap accepted"); }
            catch (ArgumentOutOfRangeException) { }
            Check(backend.Allocations == 0, "Invalid bitmap allocated a GPU texture");
        }
        using (var valid = RasterCodec.FromPixels(new byte[] { 255, 0, 0, 255 }, 1, 1))
        {
            try { using var texture = Texture.FromImage(device, valid); throw new Exception("Upload failure hidden"); }
            catch (IOException error) when (error.Message == "deliberate upload failure") { }
            Check(backend.Allocations == 1 && backend.Texture.Disposed == 1, "Upload failure did not release its GPU texture exactly once");
        }
        Console.WriteLine($"PASS raster boundaries: {checks} assertions");
        return checks;
    }

    private sealed class FailingTexture : IRenderTexture
    {
        public int Disposed;
        public IRenderHandle Handle => throw new NotSupportedException();
        public int Width => 1;
        public int Height => 1;
        public TextureFormat Format => TextureFormat.Rgba8;
        public void Upload(ReadOnlySpan<byte> pixels, int rowPitch) => throw new IOException("deliberate upload failure");
        public void SetSamplerState(SamplerStateBlock state) => throw new NotSupportedException();
        public void Dispose() => Disposed++;
    }

    private sealed class FailingUploadBackend : IRenderBackend
    {
        public int Allocations;
        public readonly FailingTexture Texture = new();
        public BackendKind Kind => BackendKind.OpenGl;
        public BackendFeatures Features => new(false, false, true, 1, "fixture");
        public IRenderTexture CreateTexture(int w, int h, TextureFormat f, ReadOnlySpan<byte> p) { Allocations++; return Texture; }
        public void Dispose() { }
        public void Resize(int w, int h) => throw new NotSupportedException();
        public IRenderTexture CreateRenderTexture(int w, int h, TextureFormat f) => throw new NotSupportedException();
        public IRenderBuffer CreateBuffer(BufferKind kind, int size) => throw new NotSupportedException();
        public IRenderShader CreateShader(ShaderSource source) => throw new NotSupportedException();
        public IRenderFramebuffer CreateFramebuffer(IRenderTexture color, DepthFormat depth, int w, int h) => throw new NotSupportedException();
        public IRenderDepthSurface CreateDepthSurface(int w, int h, DepthFormat f) => throw new NotSupportedException();
        public void AttachDepth(IRenderDepthSurface depth) => throw new NotSupportedException();
        public (bool DepthTest, bool CullFace) QueryRasterState() => throw new NotSupportedException();
        public void SetRasterState(bool depth, bool cull) => throw new NotSupportedException();
        public void SetCapability(BackendCapability capability, bool enabled) => throw new NotSupportedException();
        public void SetBlendState(BlendState state) => throw new NotSupportedException();
        public void BindFramebuffer(IRenderFramebuffer framebuffer) => throw new NotSupportedException();
        public void RestoreFramebufferBindings(IRenderFramebuffer draw, IRenderFramebuffer read) => throw new NotSupportedException();
        public void SetReadBuffer(int attachment) => throw new NotSupportedException();
        public IRenderFramebuffer CurrentDrawFramebuffer => null;
        public IRenderFramebuffer CurrentReadFramebuffer => null;
        public void SetViewport(int x, int y, int w, int h, float min, float max) => throw new NotSupportedException();
        public void Clear(ClearMask mask, float r, float g, float b, float a, float d, int s) => throw new NotSupportedException();
        public void SetScissor(bool enabled, int x, int y, int w, int h) => throw new NotSupportedException();
        public void SetDepthTest(bool enabled) => throw new NotSupportedException();
        public void SetDepthWrite(bool enabled) => throw new NotSupportedException();
        public void SetCullMode(CullMode mode) => throw new NotSupportedException();
        public void SetBlend(BlendState state) => throw new NotSupportedException();
        public void SetSamplerState(SamplerStateBlock state) => throw new NotSupportedException();
        public void SetUniformBlock(int binding, IRenderBuffer buffer, ReadOnlySpan<byte> data) => throw new NotSupportedException();
        public void BindTexture(IRenderTexture texture) => throw new NotSupportedException();
        public void Draw(IRenderShader shader, IRenderBuffer vertices, VertexLayout layout, PrimitiveKind kind, int start, int count) => throw new NotSupportedException();
        public byte[] ReadPixels(int x, int y, int w, int h) => throw new NotSupportedException();
        public bool TryGetError(out string message) { message = ""; return false; }
    }
}
