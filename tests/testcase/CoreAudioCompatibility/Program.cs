using System.Reflection;
using System.Runtime.InteropServices;
using THSSS.Audio;

if (args.Length != 1) throw new ArgumentException("Pass AudioQueue software-driver library.");
nint library = NativeLibrary.Load(Path.GetFullPath(args[0]));
NativeLibrary.SetDllImportResolver(typeof(CoreAudioBackend).Assembly, (name, _, _) => name.EndsWith("AudioToolbox") ? library : 0);
var prefetch = Marshal.GetDelegateForFunctionPointer<QueueAction>(NativeLibrary.GetExport(library, "shim_prefetch"));
var advance = Marshal.GetDelegateForFunctionPointer<Advance>(NativeLibrary.GetExport(library, "shim_advance"));
var metric = Marshal.GetDelegateForFunctionPointer<Metric>(NativeLibrary.GetExport(library, "shim_metric"));
var sample = Marshal.GetDelegateForFunctionPointer<Sample>(NativeLibrary.GetExport(library, "shim_sample"));
var failEnqueue = Marshal.GetDelegateForFunctionPointer<QueueFailure>(NativeLibrary.GetExport(library, "shim_fail_next_enqueue"));
int checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
void Near(float actual, float expected, string label) => Check(Math.Abs(actual - expected) < 0.000001f, label);
nint Queue(CoreAudioBackend backend) => (nint)typeof(CoreAudioBackend).GetField("_queue", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(backend)!;
void Render(nint queue, int count) { for (int i = 0; i < count; i++) prefetch(queue); }
Check(Marshal.SizeOf<CoreAudioBackend.StreamDescription>() == 40, "ASBD ABI");
Check(Marshal.SizeOf<CoreAudioBackend.TimeStamp>() == 64, "timestamp ABI");
Check(Marshal.OffsetOf<CoreAudioBackend.QueueBuffer>("AudioData").ToInt32() == 8, "buffer pointer ABI");

// Audible progress comes from output time, independently of buffer acquisition.
using (var backend = new CoreAudioBackend())
{
    using var source = backend.CreateSource(22050);
    nint queue = Queue(backend);
    source.Submit(new float[400], 22050);
    source.Submit(new float[600], 22050);
    source.SetGain(.5f);
    source.Play();
    Render(queue, 2);
    Check(source.SampleOffset == 0 && source.ProcessedBuffers == 0, "prefetch is not audible completion");
    int clock = CoreAudioBackend.BufferCount * CoreAudioBackend.BufferFrames + 140;
    advance(queue, clock);
    Check(source.SampleOffset == 70, "resampled partial cursor");
    source.Pause();
    advance(queue, 40); clock += 40;
    Check(source.SampleOffset == 70, "paused cursor frozen while shared device advances");
    source.Play();
    int resumeStart = (CoreAudioBackend.BufferCount + 2) * CoreAudioBackend.BufferFrames;
    Render(queue, 2);
    advance(queue, resumeStart + 400 - clock); clock = resumeStart + 400;
    Check(source.ProcessedBuffers == 1 && source.SampleOffset == 270, "resume and retained completed chunk");
    source.UnqueueProcessed();
    Check(source.SampleOffset == 70, "retirement subtracts once");
    Render(queue, 2);
    advance(queue, resumeStart + 860 - clock); clock = resumeStart + 860;
    Check(source.ProcessedBuffers == 1, "second chunk completion");
    source.UnqueueProcessed();
    Check(source.SampleOffset == 0 && !source.IsPlaying, "queue drained");

    source.Clear();
    Check(source.ProcessedBuffers == 0 && source.SampleOffset == 0, "logical clear resets cursor");
    source.Submit(new float[100], 22050);
    source.Play();
    int replayStart = (CoreAudioBackend.BufferCount + 6) * CoreAudioBackend.BufferFrames;
    Render(queue, 1);
    advance(queue, replayStart + 100 - clock);
    Check(source.SampleOffset == 50 && source.ProcessedBuffers == 1, "new playback after clear");
    for (int i = 0; i < 20; i++)
    {
        source.Pause(); source.Clear();
        source.Submit(new float[100], 22050); source.Play();
    }
    Check(metric(0) == 1 && metric(1) == 1 && metric(5) == CoreAudioBackend.BufferCount,
        "repeated sound restarts reuse one native queue, one native start and fixed buffers");
    Check(metric(2) == 0 && metric(3) == 0 && metric(4) == 0, "voice operations never pause, stop or reset native output");
    source.Dispose(); source.Dispose();
    try { source.Play(); throw new Exception("disposed source accepted"); }
    catch (ObjectDisposedException) { checks++; }
    backend.Dispose(); backend.Dispose();
    Check(metric(6) == 0, "shared queue disposed once");
}

// Check actual submitted PCM: snapshot ownership, summation, gain and interpolation.
using (var backend = new CoreAudioBackend())
{
    using var fullRate = backend.CreateSource(44100);
    using var halfRate = backend.CreateSource(22050);
    using var otherRate = backend.CreateSource(48000);
    float[] constant = new float[512 * 2], ramp = new float[256 * 2];
    for (int i = 0; i < 512; i++) { constant[i * 2] = .25f; constant[i * 2 + 1] = -.5f; }
    for (int i = 0; i < 256; i++) { ramp[i * 2] = i / 1024f; ramp[i * 2 + 1] = -i / 2048f; }
    fullRate.Submit(constant, 44100); fullRate.SetGain(.5f); fullRate.Play();
    halfRate.Submit(ramp, 22050); halfRate.Play();
    otherRate.Submit(new float[480 * 2], 48000); otherRate.Play();
    Array.Clear(constant);
    nint queue = Queue(backend);
    Render(queue, CoreAudioBackend.BufferCount + 1);
    Near(sample(0, 0), .125f, "submitted PCM is copied before caller reuse");
    Near(sample(0, 1), -.25f, "per-voice stereo gain");
    Near(sample(1, 0), .125f + 1f / 2048f, "half-rate linear interpolation");
    Near(sample(255, 1), -.25f - 127.5f / 2048f, "independent channels mixed");
    advance(queue, CoreAudioBackend.BufferCount * CoreAudioBackend.BufferFrames + 512);
    Check(fullRate.ProcessedBuffers == 1 && fullRate.SampleOffset == 512, "full-rate exact completion");
    Check(halfRate.ProcessedBuffers == 1 && halfRate.SampleOffset == 256, "half-rate exact completion");
    Check(otherRate.ProcessedBuffers == 1 && otherRate.SampleOffset == 480, "48000Hz exact completion on 44100Hz output");
}

using (var backend = new CoreAudioBackend())
{
    using var first = backend.CreateSource(44100);
    using var second = backend.CreateSource(44100);
    first.Submit(Enumerable.Repeat(.2f, 4096).ToArray(), 44100);
    second.Submit(Enumerable.Repeat(.3f, 4096).ToArray(), 44100);
    first.Play(); second.Play();
    nint queue = Queue(backend);
    Render(queue, CoreAudioBackend.BufferCount + 1);
    Near(sample(0, 0), .5f, "concurrent voices summed");
    first.Clear();
    Render(queue, CoreAudioBackend.BufferCount + 1);
    Near(sample(0, 0), .3f, "clearing one voice leaves the other voice playing");
    Check(metric(2) == 0 && metric(3) == 0 && metric(4) == 0, "clearing one voice does not reset shared output");
}

// The native callback cannot throw across P/Invoke, but the failure must stay visible.
using (var backend = new CoreAudioBackend())
{
    using var source = backend.CreateSource(44100);
    failEnqueue();
    prefetch(Queue(backend));
    try { source.Play(); throw new Exception("callback failure hidden"); }
    catch (InvalidOperationException error) { Check(error.Message.Contains("OSStatus=-9"), "callback failure rethrown"); }
}
Check(metric(6) == 0, "all native queues released");
Console.WriteLine($"PASS CoreAudioCompatibility: {checks} native ABI/mixer software-driver checks; not Apple runtime");

[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void QueueAction(nint queue);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Advance(nint queue, int frames);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Metric(int index);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate float Sample(int frame, int channel);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void QueueFailure();
