#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
#if THSSS_MACOS || THSSS_COREAUDIO_TEST
namespace THSSS.Audio;

/// <summary>One continuous AudioQueue output; individual voices are mixed without device restarts.</summary>
internal sealed unsafe class CoreAudioBackend : IAudioBackend
{
    internal const int OutputRate = 44100;
    internal const int BufferFrames = 256;
    internal const int BufferCount = 3;
    private static readonly bool TraceInput = Environment.GetEnvironmentVariable("THSSS_DIAG_INPUT") == "1";
    private readonly object _sync = new();
    private readonly HashSet<Source> _sources = new();
    private readonly OutputCallback _callback;
    private readonly nint _queue;
    private long _renderedFrames, _audibleFrame;
    private int _nextSourceId;
    private bool _disposing, _disposed;
    private ExceptionDispatchInfo? _callbackFailure;

    public string Name => "coreaudio";

    public CoreAudioBackend()
    {
#if !THSSS_COREAUDIO_TEST
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("CoreAudio requires macOS.");
#endif
        _callback = OnOutput;
        var format = new StreamDescription
        {
            SampleRate = OutputRate, FormatId = 0x6c70636d, FormatFlags = 9,
            BytesPerPacket = 8, FramesPerPacket = 1, BytesPerFrame = 8, ChannelsPerFrame = 2, BitsPerChannel = 32
        };
        Check(AudioQueueNewOutput(ref format, _callback, 0, 0, 0, 0, out _queue), "NewOutput");
        try
        {
            for (int i = 0; i < BufferCount; i++)
            {
                Check(AudioQueueAllocateBuffer(_queue, BufferFrames * 2 * sizeof(float), out nint buffer), "AllocateBuffer");
                RenderAndEnqueue(buffer);
            }
            long start = TraceInput ? Stopwatch.GetTimestamp() : 0;
            Check(AudioQueueStart(_queue, 0), "Start output");
            if (TraceInput)
                Console.Error.WriteLine($"[AUDIO-OUTPUT] rate={OutputRate} buffers={BufferCount} framesPerBuffer={BufferFrames} startMs={Stopwatch.GetElapsedTime(start).TotalMilliseconds:F2}");
        }
        catch (Exception error)
        {
            lock (_sync) _disposing = true;
            int status = AudioQueueDispose(_queue, 1);
            GC.KeepAlive(_callback);
            if (status != 0)
                throw new AggregateException(error, new InvalidOperationException($"CoreAudio Dispose after initialization failure: OSStatus={status}."));
            throw;
        }
    }

    public IAudioSource CreateSource(int sampleRate)
    {
        lock (_sync)
        {
            EnsureUsable();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
            var source = new Source(this, sampleRate, ++_nextSourceId);
            _sources.Add(source);
            return source;
        }
    }

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(_disposed || _disposing, this);
        _callbackFailure?.Throw();
    }

    private void OnOutput(nint context, nint queue, nint buffer)
    {
        try
        {
            lock (_sync)
            {
                if (_disposing || _disposed || _callbackFailure != null) return;
                RenderAndEnqueue(buffer);
            }
        }
        catch (Exception error)
        {
            // Managed exceptions must not cross the native callback boundary. The next
            // public operation rethrows the captured failure, with its original stack.
            lock (_sync) _callbackFailure ??= ExceptionDispatchInfo.Capture(error);
            Console.Error.WriteLine("[AUDIO-ERROR] CoreAudio output callback: " + error);
        }
    }

    private void RenderAndEnqueue(nint buffer)
    {
        var header = (QueueBuffer*)buffer;
        var samples = new Span<float>((void*)header->AudioData, BufferFrames * 2);
        samples.Clear();
        foreach (var source in _sources) source.Mix(samples, _renderedFrames);
        for (int i = 0; i < samples.Length; i++) samples[i] = Math.Clamp(samples[i], -1f, 1f);
        header->Size = BufferFrames * 2 * sizeof(float);
        Check(AudioQueueEnqueueBuffer(_queue, buffer, 0, 0), "EnqueueBuffer");
        _renderedFrames += BufferFrames;
    }

    private long ReadOutputFrame()
    {
        Check(AudioQueueGetCurrentTime(_queue, 0, out var time, out _), "GetCurrentTime");
        if ((time.Flags & 1) != 0) _audibleFrame = Math.Max(_audibleFrame, (long)time.SampleTime);
        return _audibleFrame;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed || _disposing) return;
            _disposing = true;
        }
        // Never hold the callback lock while waiting for native callback completion.
        Check(AudioQueueDispose(_queue, 1), "Dispose");
        lock (_sync)
        {
            foreach (var source in _sources) source.Release();
            _sources.Clear();
            _disposed = true;
        }
        GC.KeepAlive(_callback);
    }

    private sealed record Chunk(float[] Samples, long Start)
    {
        internal long End => Start + Samples.Length / 2;
    }

    // Source phase is measured in 1/OutputRate of a source frame. Integer phase
    // preserves exact completion at arbitrary source rates without cumulative drift.
    private readonly record struct RenderedSpan(long OutputStart, int OutputFrames, long SourceStart, long SourceEnd);

    private sealed class Source : IAudioSource
    {
        private readonly CoreAudioBackend _owner;
        private readonly int _rate, _id;
        private readonly List<Chunk> _chunks = new();
        private readonly Queue<RenderedSpan> _rendered = new(8);
        private long _retired, _submitted, _playedPhase, _renderedPhase;
        private float _gain = 1f;
        private bool _playing, _disposed;

        internal Source(CoreAudioBackend owner, int rate, int id) { _owner = owner; _rate = rate; _id = id; }

        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _owner.EnsureUsable();
        }

        private void UpdatePlayed()
        {
            if (!_playing || _rendered.Count == 0) return;
            long outputFrame = _owner.ReadOutputFrame();
            while (_rendered.TryPeek(out var span) && outputFrame > span.OutputStart)
            {
                long frames = Math.Min(span.OutputFrames, outputFrame - span.OutputStart);
                _playedPhase = Math.Max(_playedPhase, Math.Min(span.SourceEnd, span.SourceStart + frames * _rate));
                if (frames < span.OutputFrames) break;
                _rendered.Dequeue();
            }
        }

        internal void Mix(Span<float> destination, long outputStart)
        {
            if (!_playing || _renderedPhase == _submitted * OutputRate) return;
            long sourceStart = _renderedPhase;
            long sourceEnd = _submitted * OutputRate;
            int frames = (int)Math.Min(BufferFrames, (sourceEnd - sourceStart + _rate - 1) / _rate);
            for (int i = 0; i < frames; i++)
            {
                long frame = _renderedPhase / OutputRate;
                float fraction = (_renderedPhase % OutputRate) / (float)OutputRate;
                var a = Sample(frame);
                var b = frame + 1 < _submitted ? Sample(frame + 1) : a;
                destination[i * 2] += (a.Left + (b.Left - a.Left) * fraction) * _gain;
                destination[i * 2 + 1] += (a.Right + (b.Right - a.Right) * fraction) * _gain;
                _renderedPhase = Math.Min(sourceEnd, _renderedPhase + _rate);
            }
            _rendered.Enqueue(new(outputStart, frames, sourceStart, _renderedPhase));
        }

        private (float Left, float Right) Sample(long frame)
        {
            foreach (var chunk in _chunks)
            {
                if (frame < chunk.Start || frame >= chunk.End) continue;
                int index = checked((int)(frame - chunk.Start) * 2);
                return (chunk.Samples[index], chunk.Samples[index + 1]);
            }
            throw new InvalidOperationException("CoreAudio mixer source frame is outside its queued PCM.");
        }

        public void Submit(float[] samples, int sampleRate)
        {
            lock (_owner._sync)
            {
                EnsureUsable();
                if (sampleRate != _rate || samples.Length == 0 || samples.Length % 2 != 0)
                    throw new ArgumentException("Expected stereo frames at source rate.");
                long end = checked(_submitted + samples.Length / 2);
                _ = checked(end * OutputRate);
                _chunks.Add(new((float[])samples.Clone(), _submitted));
                _submitted = end;
            }
        }

        public int ProcessedBuffers
        {
            get
            {
                lock (_owner._sync)
                {
                    EnsureUsable(); UpdatePlayed();
                    int count = 0;
                    foreach (var chunk in _chunks) { if (chunk.End * OutputRate > _playedPhase) break; count++; }
                    return count;
                }
            }
        }

        public void UnqueueProcessed()
        {
            lock (_owner._sync)
            {
                EnsureUsable(); UpdatePlayed();
                if (_chunks.Count == 0 || _chunks[0].End * OutputRate > _playedPhase)
                    throw new InvalidOperationException("No completed CoreAudio buffer.");
                _retired = _chunks[0].End;
                _chunks.RemoveAt(0);
            }
        }

        public int SampleOffset
        {
            get { lock (_owner._sync) { EnsureUsable(); UpdatePlayed(); return checked((int)(_playedPhase / OutputRate - _retired)); } }
        }

        public bool IsPlaying
        {
            get { lock (_owner._sync) { EnsureUsable(); UpdatePlayed(); return _playing && _playedPhase < _submitted * OutputRate; } }
        }

        public void Play()
        {
            long start = TraceInput ? Stopwatch.GetTimestamp() : 0;
            lock (_owner._sync) { EnsureUsable(); _playing = true; }
            TraceTiming("Start", start);
        }

        public void Pause()
        {
            long start = TraceInput ? Stopwatch.GetTimestamp() : 0;
            lock (_owner._sync)
            {
                EnsureUsable();
                if (!_playing) return;
                UpdatePlayed();
                _playing = false;
                _renderedPhase = _playedPhase;
                _rendered.Clear();
            }
            TraceTiming("Pause", start);
        }

        public void Clear()
        {
            long start = TraceInput ? Stopwatch.GetTimestamp() : 0;
            lock (_owner._sync)
            {
                EnsureUsable();
                _playing = false;
                _chunks.Clear(); _rendered.Clear();
                _retired = _submitted = _playedPhase = _renderedPhase = 0;
            }
            TraceTiming("Clear", start);
        }

        public void SetGain(float gain)
        {
            if (!float.IsFinite(gain) || gain < 0) throw new ArgumentOutOfRangeException(nameof(gain));
            lock (_owner._sync) { EnsureUsable(); _gain = gain; }
        }

        private void TraceTiming(string operation, long start)
        {
            if (TraceInput)
                Console.Error.WriteLine($"[AUDIO-TIMING] op={operation} ms={Stopwatch.GetElapsedTime(start).TotalMilliseconds:F2} source={_id} transport=shared");
        }

        internal void Release() { _disposed = true; _chunks.Clear(); _rendered.Clear(); }

        public void Dispose()
        {
            lock (_owner._sync)
            {
                if (_disposed) return;
                Release();
                _owner._sources.Remove(this);
            }
        }
    }

    private const string Library = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    [StructLayout(LayoutKind.Sequential)] internal struct StreamDescription { public double SampleRate; public uint FormatId, FormatFlags, BytesPerPacket, FramesPerPacket, BytesPerFrame, ChannelsPerFrame, BitsPerChannel, Reserved; }
    [StructLayout(LayoutKind.Sequential)] internal struct QueueBuffer { public uint Capacity; public nint AudioData; public uint Size; public nint UserData; public uint PacketCapacity; public nint Packets; public uint PacketCount; }
    [StructLayout(LayoutKind.Sequential)] internal struct TimeStamp { public double SampleTime; public ulong HostTime; public double RateScalar; public ulong WordClockTime; public short Subframes, SubframeDivisor; public uint Counter, Type, SmpteFlags; public short Hours, Minutes, Seconds, Frames; public uint Flags, Reserved; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void OutputCallback(nint context, nint queue, nint buffer);
    [DllImport(Library)] private static extern int AudioQueueNewOutput(ref StreamDescription format, OutputCallback callback, nint context, nint runLoop, nint mode, uint flags, out nint queue);
    [DllImport(Library)] private static extern int AudioQueueAllocateBuffer(nint queue, uint size, out nint buffer);
    [DllImport(Library)] private static extern int AudioQueueEnqueueBuffer(nint queue, nint buffer, uint packets, nint descriptions);
    [DllImport(Library)] private static extern int AudioQueueStart(nint queue, nint time);
    [DllImport(Library)] private static extern int AudioQueueDispose(nint queue, byte immediately);
    [DllImport(Library)] private static extern int AudioQueueGetCurrentTime(nint queue, nint timeline, out TimeStamp time, out byte discontinuity);
    private static void Check(int status, string operation)
    {
        if (status != 0) throw new InvalidOperationException($"CoreAudio {operation} failed (OSStatus={status}).");
    }
}
#endif
