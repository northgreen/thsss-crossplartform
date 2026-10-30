// Cross-platform entry point: one GLFW/OpenGL window replaces the WinForms
// RenderForm + Direct3D9 device the original game created.
using System;
using System.IO;
using System.Runtime.ExceptionServices;
using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using SlimDX.Direct3D9;
using SlimDX.DirectInput;
using Silk.NET.Input;
using Device = SlimDX.Direct3D9.Device;
using Key = Silk.NET.Input.Key;

namespace Shooting;

public static class Program
{
    private static IWindow _window;
    private static GL _gl;
    private static IInputContext _input;
    private static WindowInputBridge _inputBridge;
    private static Game_Main _game;
    private static int _frame;
    private static ExceptionDispatchInfo _renderFailure;

    [STAThread]
    public static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Environment.ExitCode = 1;
        }
    }

    private static void Run(string[] args)
    {
        // Story scripts, CSVs and setting files are GBK (codepage 936).
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        var dataRoot = Environment.GetEnvironmentVariable("THSSS_DATA_ROOT");
        if (string.IsNullOrEmpty(dataRoot)) dataRoot = AppContext.BaseDirectory;
        using var instance = new DataRootInstance(dataRoot);
        Directory.SetCurrentDirectory(dataRoot);
        foreach (string directory in new[] { "Replay", "ScreenShot", "BPic" }) Directory.CreateDirectory(directory);

        var options = WindowOptions.Default;
        options.Title = "THSSS - Touhou Hisou Sousou";
        options.IsVisible = Environment.GetEnvironmentVariable("THSSS_WINDOW_HIDDEN") != "1";
        options.Size = new Silk.NET.Maths.Vector2D<int>(640, 480);
        options.VSync = false;
        options.WindowBorder = WindowBorder.Resizable;
        options.API = THSSS.Graphics.RenderBackendSelection.Requested == THSSS.Graphics.BackendKind.OpenGl
            ? GraphicsAPI.Default : GraphicsAPI.None;
        // macOS only offers forward-compatible core profiles and caps OpenGL at 4.1, so ask
        // for 4.1 core there. Other desktops keep the default context.
        if (OperatingSystem.IsMacOS()
            && THSSS.Graphics.RenderBackendSelection.Requested == THSSS.Graphics.BackendKind.OpenGl)
            options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core,
                ContextFlags.ForwardCompatible, new APIVersion(4, 1));
        options.PreferredDepthBufferBits = 24;
        options.PreferredStencilBufferBits = 8;
        options.Samples = new INI_RW("Setting.INI").IniReadValue("DirectX", "AntiAlias") == "1" ? 4 : 0;
        options.FramesPerSecond = 0;
        options.UpdatesPerSecond = 0;

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        try
        {
            _window.Run();
            _renderFailure?.Throw();
        }
        finally
        {
            _inputBridge?.Dispose();
            _game?.Form_Main.CompleteClose();
            _game?.Form_Main.DetachWindow();
            _input?.Dispose();
            _window.Dispose();
            _gl?.Dispose();
            KeyboardInput.Clear();
        }
    }

    private static void OnLoad()
    {
        THSSS.Platform.GlfwNative.ConstrainGameAspect(_window);
        if (THSSS.Graphics.RenderBackendSelection.Requested == THSSS.Graphics.BackendKind.OpenGl)
        {
            _gl = _window.CreateOpenGL();
            Device.CurrentContext = _gl;
        }
        // Resolve the native presentation handles before the game creates its device.
        var pointer = THSSS.Platform.GlfwNative.WindowPointer(_window);
        if (THSSS.Graphics.RenderBackendSelection.Requested == THSSS.Graphics.BackendKind.Metal)
            THSSS.Graphics.RenderBackendSelection.CocoaWindow = THSSS.Platform.GlfwNative.CocoaWindow(pointer);
        THSSS.Graphics.RenderBackendSelection.SurfaceSource =
            THSSS.Platform.GlfwNative.Resolve(pointer);
        if (Environment.GetEnvironmentVariable("THSSS_DIAG_VK") == "1")
            Console.WriteLine($"[VK] platform={THSSS.Platform.GlfwNative.ActivePlatform()} pointer=0x{pointer:x} resolved={(THSSS.Graphics.RenderBackendSelection.SurfaceSource is null ? "no" : "yes")}");
        if (THSSS.Graphics.RenderBackendSelection.Requested == THSSS.Graphics.BackendKind.Vulkan
            && THSSS.Graphics.RenderBackendSelection.SurfaceSource == null)
            throw new PlatformNotSupportedException(
                "The selected window backend did not provide a Vulkan presentation surface.");
        _input = _window.CreateInput();
        _game = new Game_Main();
        if (!_game.initSuccess) throw new InvalidOperationException("Game_Main initialisation failed; see the diagnostic message.");
        _game.Form_Main.AttachWindow(_window);
        // Presenting backends create their swapchain here, once the window exists.
        var size = _window.FramebufferSize;
        _game.DeviceMain.RenderBackend.AttachPresentSurface(
            size.X > 0 ? size.X : _game.DeviceMain.GameWidth,
            size.Y > 0 ? size.Y : _game.DeviceMain.GameHeight);
        _inputBridge = new WindowInputBridge(_game.Form_Main, _input);
        _inputBridge.DevicesChanged += _game.RefreshInputDevices;
        _game.RefreshInputDevices();
        _inputBridge.ApplyCursor();

        // Verification hook: THSSS_START_STATE=All skips straight to the ending so
        // the dialogue box can be checked without playing through a stage.
        var startState = Environment.GetEnvironmentVariable("THSSS_START_STATE");
        if (_game.initSuccess && !string.IsNullOrEmpty(startState))
            _game.RequestState(startState);
    }

    private static readonly System.Diagnostics.Stopwatch FrameClock = new();
    private static readonly bool TraceFps = Environment.GetEnvironmentVariable("THSSS_DIAG_FPS") == "1";
    private static int _performanceFrames;
    private static double _renderMilliseconds;
#if THSSS_MACOS
    private static long _metalSubmissions, _metalDraws, _metalPasses, _metalUploadBytes;
    private static double _metalWaitMilliseconds;
#endif

    private static void OnRender(double deltaTime)
    {
        if (_renderFailure != null) return;
        try { RenderFrame(deltaTime); }
        catch (Exception error)
        {
            // Silk 2.23 clears its render-loop flag only after callbacks return.
            // Let it unwind normally, then rethrow with the original stack in Run.
            _renderFailure = ExceptionDispatchInfo.Capture(error);
            _window.IsClosing = true;
        }
    }

    private static void RenderFrame(double deltaTime)
    {
        if (_game == null || _window.IsClosing) return;
        _inputBridge.ApplyCursor();
        _game.Form_Main.AllowAspectCorrection = THSSS.Platform.GlfwNative.ActivePlatform() != THSSS.Platform.GlfwNative.Platform.Wayland;
        _game.Form_Main.EnsureWindowAspect();

        // The logical surface is fixed at 640x480; publish the real window size so
        // the sprite batcher can letterbox it exactly like the original window.
        var framebuffer = _window.FramebufferSize;
        if (_window.WindowState == Silk.NET.Windowing.WindowState.Minimized
            || !_game.DeviceMain.ResizeFramebuffer(framebuffer.X, framebuffer.Y)) return;

        _frame++;

        // Verification hook: inject scripted key presses so the input path can be
        // exercised without a human at the keyboard. Format: "frame:key,frame:key".
        InjectScriptedInput(_frame);
        if (_window.IsClosing) return;

        // MainProcess owns update + clear + draw for the whole frame, so it has to run
        // inside the render callback: GLFW presents the buffer right after it returns.
        if (TraceFps && !FrameClock.IsRunning) FrameClock.Start();
        long renderStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var backend = _game.DeviceMain.RenderBackend;
        backend.BeginFrame();
        try { _game.MainProcess(); }
        finally { backend.EndFrame(); }
        if (TraceFps)
        {
            _renderMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds;
#if THSSS_MACOS
            if (backend is THSSS.Graphics.Metal.MetalRenderBackend metal)
            {
                _metalSubmissions += metal.FrameSubmissions;
                _metalDraws += metal.FrameDraws;
                _metalPasses += metal.FramePasses;
                _metalUploadBytes += metal.FrameUploadBytes;
                _metalWaitMilliseconds += metal.FrameWaitMilliseconds;
            }
#endif
            _performanceFrames++;
            double seconds = FrameClock.Elapsed.TotalSeconds;
            if (seconds >= 1.0)
            {
                double frames = _performanceFrames;
                string details = backend is THSSS.Graphics.Vulkan.VulkanRenderBackend vk
                    ? $" submits={vk.FrameSubmissions} draws={vk.FrameDraws}" : "";
#if THSSS_MACOS
                if (backend is THSSS.Graphics.Metal.MetalRenderBackend)
                {
                    details = $" submits={_metalSubmissions / frames:F2} draws={_metalDraws / frames:F1}" +
                        $" passes={_metalPasses / frames:F1} waitMs={_metalWaitMilliseconds / frames:F2}" +
                        $" uploadKiB={_metalUploadBytes / frames / 1024:F1}";
                }
                _metalSubmissions = _metalDraws = _metalPasses = _metalUploadBytes = 0;
                _metalWaitMilliseconds = 0;
#endif
                Console.WriteLine($"[FPS] frame={_frame} fps={frames / seconds:F2} mainMs={_renderMilliseconds / frames:F2}" +
                    $" renderer={backend.Kind}" + details);
                _performanceFrames = 0; _renderMilliseconds = 0; FrameClock.Restart();
            }
        }
        _inputBridge.ApplyCursor();
        if (_window.IsClosing) return;
        // P requests are captured only after this frame is drawn and before GLFW swaps.
        _game.CapturePendingScreenshot();

        var shotPath = Environment.GetEnvironmentVariable("THSSS_SCREENSHOT");
        if (!string.IsNullOrEmpty(shotPath) && _frame == int.Parse(
                Environment.GetEnvironmentVariable("THSSS_SHOT_FRAME") ?? "120"))
        {
            SaveScreenshot(shotPath);
        }
    }

    private static string[] _scriptedInput;
    private static int _scriptedIndex;

    /// <summary>
    /// Feeds the same KeyboardInput state the real keyboard events write to, at the
    /// frames listed in THSSS_INPUT ("180:Down,200:Enter,..."). Each key is held for
    /// a few frames then released, mimicking a tap.
    /// </summary>
    private static Silk.NET.Input.Key _heldKey;
    private static bool _holdingKey;
    private static int _releaseAtFrame;

    private static void InjectScriptedInput(int frame)
    {
        // A scripted key must be released again: the game only reacts on the
        // rising edge (its "LastZ == 0" style debounce ignores a stuck key).
        if (_holdingKey && frame >= _releaseAtFrame)
        {
            _inputBridge.PublishKey(null, _heldKey, false);
            _holdingKey = false;
        }

        _scriptedInput ??= (Environment.GetEnvironmentVariable("THSSS_INPUT") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (_scriptedIndex >= _scriptedInput.Length) return;

        var parts = _scriptedInput[_scriptedIndex].Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int at)) return;
        if (frame < at) return;

        if (Enum.TryParse<Key>(parts[1], true, out var key))
        {
            _inputBridge.PublishKey(null, key, true);
            _heldKey = key;
            _holdingKey = true;
            _releaseAtFrame = frame + 4;
            Console.WriteLine($"[INPUT] frame={frame} press {key}");
        }
        _scriptedIndex++;
    }

    private static unsafe void SaveScreenshot(string path)
    {
        var size = _window.FramebufferSize;
        int width = size.X, height = size.Y;
        var pixels = new byte[width * height * 4];
        if (_gl != null)
        {
            fixed (byte* data = pixels)
                _gl.ReadPixels(0, 0, (uint)width, (uint)height,
                    Silk.NET.OpenGL.GLEnum.Rgba, Silk.NET.OpenGL.GLEnum.UnsignedByte, data);
        }
        else
        {
            var backend = _game.DeviceMain.RenderBackend;
            var draw = backend.CurrentDrawFramebuffer;
            var read = backend.CurrentReadFramebuffer;
            try
            {
                backend.RestoreFramebufferBindings(draw, null);
                pixels = backend.ReadPixels(0, 0, width, height);
            }
            finally { backend.RestoreFramebufferBindings(draw, read); }
        }
        int nonBlack = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0) nonBlack++;
            pixels[i + 3] = 255;
        }
        // ReadPixels returns rows bottom-up; flip so the PNG matches the screen.
        int stride = width * 4;
        var flipped = new byte[pixels.Length];
        for (int row = 0; row < height; row++)
            System.Buffer.BlockCopy(pixels, (height - 1 - row) * stride, flipped, row * stride, stride);
        using var image = SixLabors.ImageSharp.Image.LoadPixelData<SixLabors.ImageSharp.PixelFormats.Rgba32>(flipped, width, height);
        using var stream = File.Create(path);
        image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        Console.WriteLine($"[SHOT] {path} {width}x{height} nonBlack={nonBlack}/{width * height}");
    }
}
