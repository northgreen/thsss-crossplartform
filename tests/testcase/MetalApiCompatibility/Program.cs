using System.Reflection;
using System.Runtime.InteropServices;
using THSSS.Graphics;
using THSSS.Graphics.Metal;

internal static class Program
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
                throw new PlatformNotSupportedException("Native shim requires Linux x86_64 SysV ABI.");
            nint shim = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "libmetal_api_shim.so"));
            NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), (name, _, _) =>
                name is "/usr/lib/libobjc.A.dylib" or "/System/Library/Frameworks/Metal.framework/Metal" or "metal_api_shim" ? shim : 0);
            Environment.SetEnvironmentVariable("THSSS_METAL_API_SHIM", "1");
            TestTextureAndClear();
            TestDrawAndFrames();
            TestErrorsAndOwnership();
            TestWindowPresentation();
            Equal(0, Native.shim_live(), "all owned native objects released");
            Console.WriteLine($"PASS: {_assertions} assertions; actual Metal backend P/Invoke, software API shim (no GPU/MSL execution).");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void TestTextureAndClear()
    {
        using var backend = new MetalRenderBackend();
        Check(backend.Features.SupportsRgb8Textures,"Metal advertises opaque RGB texture semantics");
        using var texture = backend.CreateRenderTexture(2, 2, TextureFormat.Rgba8);
        byte[] padded = { 1,2,3,4, 5,6,7,8, 99,99,99,99, 9,10,11,12, 13,14,15,16 };
        texture.Upload(padded, 12);
        using var framebuffer = backend.CreateFramebuffer(texture, DepthFormat.Depth24, 2, 2);
        backend.BindFramebuffer(framebuffer);
        Bytes(new byte[] { 1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16 }, backend.ReadPixels(0,0,2,2), "padded upload roundtrip");
        Bytes(new byte[] { 13,14,15,16 }, backend.ReadPixels(1,1,1,1), "region ABI nonzero origin");
        Native.shim_clear_trace();
        backend.Clear(ClearMask.Color | ClearMask.Depth | ClearMask.Stencil, 1, 0.5f, 0.25f, 1, 0.375f, 7);
        Bytes(Enumerable.Repeat(new byte[] {255,128,64,255},4).SelectMany(x=>x).ToArray(), backend.ReadPixels(0,0,2,2), "native clear/readback");
        Equal(0.375, Native.shim_depth(), "double clear-depth ABI");
        Equal(1L, backend.TotalSubmissions, "clear submission counted");
        Ordered("commandBuffer", "renderPassDescriptor", "setClearColor:", "setClearDepth:", "renderCommandEncoderWithDescriptor:", "endEncoding", "commit", "waitUntilCompleted", "status", "getBytes:bytesPerRow:fromRegion:mipmapLevel:");
        backend.Clear(ClearMask.Depth, 0,0,0,0,1,0);
        Bytes(new byte[] {255,128,64,255}, backend.ReadPixels(0,0,1,1), "depth-only clear preserves color");
        using var bgra = backend.CreateTexture(1,1,TextureFormat.Bgra8,new byte[] {3,2,1,4});
        using var bgraFrame = backend.CreateFramebuffer(bgra,DepthFormat.None,1,1);
        backend.BindFramebuffer(bgraFrame);
        Bytes(new byte[] {1,2,3,4},backend.ReadPixels(0,0,1,1),"BGRA upload conversion");
        backend.Clear(ClearMask.Color,1,0,0,1,1,0);
        Bytes(new byte[] {255,0,0,255},backend.ReadPixels(0,0,1,1),"BGRA clear conversion");
        using var rgb = backend.CreateTexture(1,2,TextureFormat.Rgb8,new byte[] {1,2,3,4,5,6});
        using var rgbFrame = backend.CreateFramebuffer(rgb,DepthFormat.None,1,2);
        backend.BindFramebuffer(rgbFrame);
        Bytes(new byte[] {1,2,3,255,4,5,6,255},backend.ReadPixels(0,0,1,2),"RGB expanded alpha");
        backend.Clear(ClearMask.Color,.25f,.5f,.75f,0,1,0);
        Bytes(new byte[] {64,128,191,255},backend.ReadPixels(0,0,1,1),"RGB clear discards requested alpha zero");
        backend.BindFramebuffer(framebuffer);
        backend.Clear(ClearMask.Color,.25f,.5f,.75f,0,1,0);
        Bytes(new byte[] {64,128,191,0},backend.ReadPixels(0,0,1,1),"RGBA clear retains requested alpha zero");
        backend.RestoreFramebufferBindings(framebuffer, bgraFrame);
        backend.Clear(ClearMask.Color,0,1,0,1,1,0);
        Bytes(new byte[] {255,0,0,255},backend.ReadPixels(0,0,1,1),"independent read binding");
        Check(ReferenceEquals(framebuffer,backend.CurrentDrawFramebuffer),"draw binding preserved");
        Check(ReferenceEquals(bgraFrame,backend.CurrentReadFramebuffer),"read binding preserved");
    }

    private static void TestDrawAndFrames()
    {
        using var backend = new MetalRenderBackend();
        backend.Resize(8,8);
        using var vertices = backend.CreateBuffer(BufferKind.Vertex,32);
        float[] triangle = { -1,-1, 1,-1, 0,1 };
        byte[] bytes = MemoryMarshal.AsBytes(triangle.AsSpan()).ToArray();
        vertices.Update(bytes,8);
        var nativeBuffer = (MetalBuffer)vertices;
        byte[] stored = new byte[32];
        Marshal.Copy(MetalNative.Call(nativeBuffer.Native,"contents"),stored,0,stored.Length);
        Bytes(bytes,stored[8..],"actual shared native buffer update offset");
        vertices.Update(bytes,0);
        using var shader = backend.CreateShader(new ShaderSource
        {
            Vertex = "#version 330 core\nin vec2 Position;\nvoid main() { gl_Position = vec4(Position, 0.0, 1.0); }",
            Fragment = "#version 330 core\nout vec4 FragColor;\nvoid main() { FragColor = vec4(0.25, 0.5, 0.75, 1.0); }",
            VertexInputs = new[] { "Position" }
        });
        Equal(2,Native.shim_libraries(),"real GLSL/SPIR-V/MSL translations reached native API");
        var layout = new VertexLayout(8,new[] {new VertexAttribute(0,2,VertexAttributeType.Float,false,0)});
        backend.SetViewport(1,2,6,5,0.25f,0.75f);
        backend.SetScissor(true,2,1,3,4);
        backend.SetUniformBlock(0,null,new byte[16]);
        Native.shim_clear_trace();
        backend.BeginFrame();
        Throws<InvalidOperationException>(()=>backend.BeginFrame(),"nested frame rejected");
        backend.Clear(ClearMask.Color,0,0,1,1,1,0);
        backend.Draw(shader,vertices,layout,PrimitiveKind.TriangleList,0,3);
        Equal(0L, backend.TotalSubmissions, "frame clear and draw do not submit separately");
        Equal(1, backend.FramePasses, "clear and same-target draw share an encoder");
        Check(!Trace.Contains("waitUntilCompleted\n", StringComparison.Ordinal), "no per-draw GPU wait inside frame");
        vertices.Update(new byte[bytes.Length], 0);
        byte[] snapshot = new byte[bytes.Length];
        Marshal.Copy(Native.shim_vertex_bytes(), snapshot, 0, snapshot.Length);
        Bytes(bytes, snapshot, "recorded draw owns a vertex snapshot before source reuse");
        vertices.Update(bytes, 0);
        Equal(1,Native.shim_draws(),"native draw reached");
        Equal((nuint)3,Native.shim_draw_arg(0),"Metal triangle primitive enum");
        Equal((nuint)0,Native.shim_draw_arg(1),"draw vertex start");
        Equal((nuint)3,Native.shim_draw_arg(2),"draw vertex count");
        Equal((nuint)30,Native.shim_draw_arg(3),"vertex buffer slot");
        double[] expectedViewport = {1,2,6,5,0.25,0.75};
        for(int i=0;i<6;i++) Equal(expectedViewport[i],Native.shim_viewport(i),$"viewport ABI component {i}");
        nuint[] expectedScissor = {2,1,3,4};
        for(int i=0;i<4;i++) Equal(expectedScissor[i],Native.shim_scissor(i),$"scissor ABI component {i}");
        // The shim records draws, not shader execution. Only clear pixels are asserted.
        Bytes(new byte[] {0,0,255,255},backend.ReadPixels(0,0,1,1),"draw load action preserves prior clear");
        Equal(1, backend.FrameSubmissions, "readback flushes the recorded commands once");
        Ordered("commandBuffer","renderCommandEncoderWithDescriptor:","newRenderPipelineStateWithDescriptor:error:","setRenderPipelineState:","setViewport:","setScissorRect:","setVertexBuffer:offset:atIndex:","setVertexBytes:length:atIndex:","setFragmentBytes:length:atIndex:","drawPrimitives:vertexStart:vertexCount:","endEncoding","commit","waitUntilCompleted","status","getBytes:bytesPerRow:fromRegion:mipmapLevel:");
        Native.shim_clear_trace();
        backend.Draw(shader,vertices,layout,PrimitiveKind.TriangleList,1,3);
        Check(!Trace.Contains("newRenderPipelineStateWithDescriptor:error:",StringComparison.Ordinal),"pipeline cache reused");
        Equal((nuint)1,Native.shim_draw_arg(1),"nonzero vertex start forwarded");
        using var rgbaTarget = backend.CreateRenderTexture(8,8,TextureFormat.Rgba8);
        using var rgbTarget = backend.CreateRenderTexture(8,8,TextureFormat.Rgb8);
        using var rgbaFrame = backend.CreateFramebuffer(rgbaTarget,DepthFormat.None,8,8);
        using var rgbFrame = backend.CreateFramebuffer(rgbTarget,DepthFormat.None,8,8);
        foreach (bool additive in new[] { false,true })
        {
            backend.SetBlendState(new(true,BlendFactor.SourceAlpha,
                additive?BlendFactor.One:BlendFactor.InverseSourceAlpha,BlendEquation.Add));
            // Both logical targets use native RGBA8 and no depth. Their alpha write
            // masks still require separate pipeline cache entries.
            var targets = new[] { rgbaFrame,rgbFrame,rgbaFrame,rgbFrame };
            for (int i=0;i<targets.Length;i++)
            {
                backend.BindFramebuffer(targets[i]);
                Native.shim_clear_trace();
                backend.Draw(shader,vertices,layout,PrimitiveKind.TriangleList,0,3);
                Equal((nuint)1,Native.shim_blend_arg(0),"blending enabled");
                Equal((nuint)4,Native.shim_blend_arg(1),"RGB still weighted by source alpha");
                Equal((nuint)(additive?1:5),Native.shim_blend_arg(2),"normal/additive destination factor");
                Equal((nuint)4,Native.shim_blend_arg(3),"alpha source factor preserved");
                Equal((nuint)(additive?1:5),Native.shim_blend_arg(4),"alpha destination factor preserved");
                Equal((nuint)0,Native.shim_blend_arg(5),"RGB add operation");
                Equal((nuint)0,Native.shim_blend_arg(6),"alpha add operation");
                Equal((nuint)(i%2==0?15:14),Native.shim_blend_arg(7),"RGBA/RGB alpha write semantics");
                if(i>=2)Check(!Trace.Contains("newRenderPipelineStateWithDescriptor:error:",StringComparison.Ordinal),"cached pipeline retains logical format write mask");
            }
        }
        backend.BindFramebuffer(null);
        long before = backend.TotalSubmissions;
        backend.Draw(shader,vertices,layout,PrimitiveKind.TriangleList,0,0);
        Equal(before,backend.TotalSubmissions,"zero-count draw submits nothing");
        backend.SetScissor(true,0,0,0,0);
        int draws = Native.shim_draws();
        backend.Draw(shader,vertices,layout,PrimitiveKind.TriangleList,0,3);
        Equal(draws,Native.shim_draws(),"empty scissor suppresses draw");
        backend.Present();
        Equal(0,backend.PresentedFrames,"headless present does not claim drawable presentation");
        Equal(2,backend.FrameSubmissions,"readback and headless frame end are the only submissions");
        backend.BeginFrame(); backend.EndFrame();
        Equal(0,backend.FrameSubmissions,"empty frame submits nothing");
        backend.Resize(3,2);
        backend.Clear(ClearMask.Color,1,0,1,1,1,0);
        Equal(24,backend.ReadPixels(0,0,3,2).Length,"resized default target");
        Throws<ArgumentException>(()=>backend.Draw(shader,vertices,layout,PrimitiveKind.TriangleList,2,3),"draw bounds rejected");
    }

    private static void TestErrorsAndOwnership()
    {
        using var backend = new MetalRenderBackend();
        using var other = new MetalRenderBackend();
        using var foreign = other.CreateRenderTexture(1,1,TextureFormat.Rgba8);
        Throws<ArgumentException>(()=>backend.BindTexture(foreign),"foreign texture rejected");
        Throws<ArgumentOutOfRangeException>(()=>backend.CreateBuffer(BufferKind.Vertex,0),"zero buffer rejected");
        Throws<ArgumentOutOfRangeException>(()=>backend.ReadPixels(-1,0,1,1),"read bounds rejected");
        Throws<ArgumentOutOfRangeException>(()=>backend.SetReadBuffer(1),"unsupported color attachment rejected");
        using var buffer = backend.CreateBuffer(BufferKind.Vertex,4);
        Throws<ArgumentOutOfRangeException>(()=>buffer.Update(new byte[5],0),"buffer overflow rejected");
        buffer.Dispose(); buffer.Dispose();
        Throws<ObjectDisposedException>(()=>buffer.Update(new byte[1],0),"disposed buffer rejected");
        Native.shim_fail_next_command();
        var failure = Throws<InvalidOperationException>(()=>backend.Clear(ClearMask.Color,0,0,0,1,1,0),"command failure surfaced");
        Check(failure.Message.Contains("injected command failure",StringComparison.Ordinal),"NSError description preserved");
        Equal(1,Native.shim_failures(),"native failure observed");
        backend.Dispose(); backend.Dispose();
        Throws<ObjectDisposedException>(()=>backend.Clear(ClearMask.Color,0,0,0,1,1,0),"disposed backend rejected");
    }

    private static void TestWindowPresentation()
    {
        Native.shim_clear_trace();
        using var backend = new MetalRenderBackend(Native.shim_window());
        Ordered("setDevice:","setPixelFormat:","setFramebufferOnly:","contentView","setWantsLayer:","setLayer:","setDrawableSize:");
        backend.Resize(1000,650);
        backend.Clear(ClearMask.Color,0.25f,0.5f,0.75f,1,1,0);
        Bytes(new byte[] {64,128,191,255},backend.ReadPixels(0,0,1,1),"default target raw clear color");
        var property = typeof(MetalRenderBackend).GetProperty("DefaultFramebuffer",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var frame = (MetalFramebuffer)property.GetValue(backend)!;
        var texture = (MetalTexture)frame.Color!;
        Equal(1000,texture.Width,"resized default width");
        Equal(650,texture.Height,"resized default height");
        byte[] corners = new byte[1000*650*4];
        byte[][] colors = {new byte[] {255,0,0,255},new byte[] {0,255,0,255},new byte[] {0,0,255,255},new byte[] {255,255,0,255}};
        (int X,int Y)[] positions = {(0,0),(999,0),(0,649),(999,649)};
        for (int i=0;i<positions.Length;i++) colors[i].CopyTo(corners,(positions[i].Y*1000+positions[i].X)*4);
        texture.Upload(corners,4000);
        for (int i=0;i<positions.Length;i++) Bytes(colors[i],backend.ReadPixels(positions[i].X,positions[i].Y,1,1),$"raw default corner {i}, not displayed pixels");
        int draws = Native.shim_draws(), presentations = Native.shim_presentations();
        long submissions = backend.TotalSubmissions;
        backend.BeginFrame();
        Native.shim_clear_trace();
        backend.Present();
        Equal((nuint)1000,Native.shim_drawable_arg(0),"drawable width");
        Equal((nuint)650,Native.shim_drawable_arg(1),"drawable height");
        Equal((nuint)80,Native.shim_drawable_arg(2),"drawable BGRA8 format");
        Equal(1,backend.PresentedFrames,"window presentation counted");
        Equal(presentations+1,Native.shim_presentations(),"native present selector counted");
        Equal(draws+1,Native.shim_draws(),"presentation triangle draw counted");
        Equal(submissions+1,backend.TotalSubmissions,"presentation submission counted");
        Equal(texture.Native,Native.shim_present_source(),"presentation samples actual default backbuffer");
        Ordered("nextDrawable","newLibraryWithSource:options:error:","newFunctionWithName:","newFunctionWithName:","newRenderPipelineStateWithDescriptor:error:","commandBuffer","texture","renderCommandEncoderWithDescriptor:","setRenderPipelineState:","setFragmentTexture:atIndex:","drawPrimitives:vertexStart:vertexCount:","endEncoding","presentDrawable:","commit","waitUntilCompleted","status","release");
        backend.BeginFrame(); backend.EndFrame();
        Native.shim_clear_trace();
        backend.Present();
        Equal(2,backend.PresentedFrames,"second drawable presented");
        Check(!Trace.Contains("newRenderPipelineStateWithDescriptor:error:",StringComparison.Ordinal),"presentation pipeline reused");
        backend.Dispose();
        Equal(0,Native.shim_live(),"window presentation releases all owned objects");
        CheckPresentationOrientation();
    }

    private static void CheckPresentationOrientation()
    {
        // This examines the actual submitted const MSL; the shim never executes shaders.
        string msl = Marshal.PtrToStringUTF8(Native.shim_present_msl()) ?? "";
        Check(msl.Contains("o.p=float4(p*2-1,0,1)",StringComparison.Ordinal),"presentation NDC mapping inspected");
        string vertexMsl = MetalShader.TranslateToMsl("#version 450\nlayout(location=0) in vec2 Position;\nvoid main() { gl_Position = vec4(Position, 0.0, 1.0); }",true);
        Check(System.Text.RegularExpressions.Regex.IsMatch(vertexMsl,@"gl_Position\.y\s*=\s*-"),"actual translated game vertex Y flip inspected");
        // GL top +1 -> flipped Metal -1 -> raw last row. Display top +1 -> p.y=1 must sample v=1.
        bool inverted = msl.Contains("o.uv=float2(p.x,1-p.y)",StringComparison.Ordinal);
        bool direct = msl.Contains("o.uv=p",StringComparison.Ordinal) || msl.Contains("o.uv=float2(p.x,p.y)",StringComparison.Ordinal);
        Check(inverted || direct,"presentation UV expression recognized for analytical check");
        double displayTopP = (1.0+1.0)/2.0;
        double sampledTopV = inverted ? 1-displayTopP : displayTopP;
        Equal(1.0,sampledTopV,"analytical orientation: display top samples game-top raw last row (not GPU evidence)");
    }

    private static string Trace => Marshal.PtrToStringUTF8(Native.shim_trace()) ?? "";
    private static void Ordered(params string[] selectors)
    {
        string trace = Trace;
        int offset = 0;
        foreach (string selector in selectors)
        {
            int next = trace.IndexOf(selector+"\n",offset,StringComparison.Ordinal);
            Check(next>=0,"native command order: "+selector+"\n"+trace);
            offset = next+selector.Length+1;
        }
    }
    private static void Bytes(byte[] expected,byte[] actual,string label) => Check(expected.AsSpan().SequenceEqual(actual),label);
    private static void Equal<T>(T expected,T actual,string label) where T : IEquatable<T> => Check(expected.Equals(actual),$"{label}: expected {expected}, got {actual}");
    private static void Check(bool condition,string label)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed: "+label);
        _assertions++;
    }
    private static T Throws<T>(Action action,string label) where T : Exception
    {
        try { action(); }
        catch(T error) { _assertions++; return error; }
        throw new InvalidOperationException("Expected "+typeof(T).Name+": "+label);
    }
}

internal static partial class Native
{
    [DllImport("metal_api_shim")] internal static extern nint shim_trace();
    [DllImport("metal_api_shim")] internal static extern void shim_clear_trace();
    [DllImport("metal_api_shim")] internal static extern int shim_live();
    [DllImport("metal_api_shim")] internal static extern int shim_failures();
    [DllImport("metal_api_shim")] internal static extern int shim_draws();
    [DllImport("metal_api_shim")] internal static extern int shim_libraries();
    [DllImport("metal_api_shim")] internal static extern nuint shim_draw_arg(int index);
    [DllImport("metal_api_shim")] internal static extern nuint shim_blend_arg(int index);
    [DllImport("metal_api_shim")] internal static extern nint shim_vertex_bytes();
    [DllImport("metal_api_shim")] internal static extern double shim_viewport(int index);
    [DllImport("metal_api_shim")] internal static extern nuint shim_scissor(int index);
    [DllImport("metal_api_shim")] internal static extern double shim_depth();
    [DllImport("metal_api_shim")] internal static extern void shim_fail_next_command();
    [DllImport("metal_api_shim")] internal static extern nint shim_window();
    [DllImport("metal_api_shim")] internal static extern int shim_presentations();
    [DllImport("metal_api_shim")] internal static extern nuint shim_drawable_arg(int index);
    [DllImport("metal_api_shim")] internal static extern nint shim_present_source();
    [DllImport("metal_api_shim")] internal static extern nint shim_present_msl();
}
