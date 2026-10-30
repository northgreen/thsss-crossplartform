// XAudio2 compatibility API with shared playback semantics and selectable native transports.
using System;
using System.Collections.Generic;
using SlimDX.Multimedia;
using THSSS.Audio;

namespace SlimDX.XAudio2
{
    public class XAudio2 : IDisposable
    {
        internal static AudioEngine Engine => AudioEngine.Instance;

        public MasteringVoice CreateMasteringVoice(int inputChannels = 0, int inputSampleRate = 0)
            => new();

        public void Dispose() { }
    }

    public class MasteringVoice : IDisposable
    {
        public MasteringVoice() { }
        public MasteringVoice(XAudio2 device) { }
        public MasteringVoice(int inputChannels) { }
        public MasteringVoice(int inputChannels, int inputSampleRate) { }
        public void Dispose() { }
    }

    public class SourceVoice : IDisposable
    {
        private readonly AudioEngine _engine = AudioEngine.Instance;
        private readonly WaveFormat _format;
        private readonly Queue<AudioPlayback> _playbacks = new();
        private readonly Queue<(AudioPlayback Playback, int Frames)> _queued = new();
        private readonly IAudioSource _output;
        private float[] _matrix;
        private float _volume = 1f;
        private long _samplesPlayed;
        private bool _running;
        private bool _disposed;
        private Exception _failure;

        public SourceVoice(XAudio2 device, WaveFormat format) : this(format) { }
        public SourceVoice(XAudio2 device, WaveFormat format, bool enableFilter) : this(format) { }
        public SourceVoice(XAudio2 device, WaveFormat format, VoiceFlags flags) : this(format) { }

        private SourceVoice(WaveFormat format)
        {
            ArgumentNullException.ThrowIfNull(format);
            if (format.Channels is < 1 or > 2 || format.SamplesPerSecond <= 0 ||
                !(format.FormatTag == (short)WaveFormatTag.Pcm && format.BitsPerSample is 8 or 16 or 24 or 32 ||
                  format.FormatTag == (short)WaveFormatTag.IeeeFloat && format.BitsPerSample == 32))
                throw new NotSupportedException("SourceVoice requires mono/stereo PCM (8/16/24/32-bit) or float32.");
            _format = new WaveFormat(format.SamplesPerSecond, format.BitsPerSample, format.Channels) { FormatTag = format.FormatTag };
            _matrix = format.Channels == 1 ? new[] { 1f, 1f } : new[] { 1f, 0f, 0f, 1f };
            lock (_engine.Sync)
            {
                _output = _engine.Backend.CreateSource(_format.SamplesPerSecond);
            }
        }

        public VoiceState State
        {
            get
            {
                lock (_engine.Sync)
                {
                    EnsureUsable();
                    Pump();
                    return new VoiceState { BuffersQueued = _playbacks.Count, SamplesPlayed = _samplesPlayed + Offset() };
                }
            }
        }
        public VoiceDetails VoiceDetails => new() { InputChannels = _format.Channels, InputSampleRate = _format.SamplesPerSecond };
        public FilterParameters FilterParameters { get; set; }
        internal int QueueDepth => (int)State.BuffersQueued;

        public void SubmitSourceBuffer(AudioBuffer audioBuffer) => SubmitSourceBuffer(audioBuffer, Array.Empty<int>());

        public void SubmitSourceBuffer(AudioBuffer audioBuffer, int[] decodedPacketCumulativeBytes)
        {
            ArgumentNullException.ThrowIfNull(audioBuffer);
            var playback = new AudioPlayback(audioBuffer, _format);
            lock (_engine.Sync)
            {
                EnsureUsable();
                _playbacks.Enqueue(playback);
                if (_running) _engine.Register(this);
                Pump();
            }
        }

        public void Start() => Start(PlayFlags.None);
        public void Start(PlayFlags flags)
        {
            lock (_engine.Sync)
            {
                EnsureUsable();
                _running = true;
                _engine.Register(this);
                Pump();
                AudioEngine.VoicesStarted++;
            }
        }

        public void Stop()
        {
            lock (_engine.Sync)
            {
                EnsureUsable();
                _output.Pause();
                _running = false;
                Pump();
                _engine.Unregister(this);
            }
        }

        public void SetVolume(float volume) => Volume = volume;
        public Volume Volume
        {
            get { lock (_engine.Sync) return new(_volume); }
            set
            {
                float gain = value;
                if (!float.IsFinite(gain) || gain < 0 || gain > 16777216f) throw new ArgumentOutOfRangeException(nameof(value));
                lock (_engine.Sync)
                {
                    EnsureUsable();
                    _volume = gain;
                    _output.SetGain(gain);
                }
            }
        }

        public void FlushSourceBuffers()
        {
            lock (_engine.Sync)
            {
                EnsureUsable();
                Pump();
                _samplesPlayed += Offset();
                ClearNativeQueue();
                _playbacks.Clear();
            }
        }

        public void SetOutputMatrix(int sourceChannels, int destinationChannels, float matrix)
            => SetOutputMatrix(sourceChannels, destinationChannels, new[] { matrix });

        public void SetOutputMatrix(int sourceChannels, int destinationChannels, float[] matrix)
        {
            ArgumentNullException.ThrowIfNull(matrix);
            if (sourceChannels != _format.Channels || destinationChannels is < 1 or > 2)
                throw new ArgumentException("Matrix dimensions must match the voice and a mono/stereo output.");
            float[] coefficients;
            if (sourceChannels == 2 && destinationChannels == 2 && matrix.Length == 3 && matrix[0] == 0f)
                coefficients = new[] { matrix[1], 0f, 0f, matrix[2] };
            else if (matrix.Length == sourceChannels * destinationChannels)
                coefficients = (float[])matrix.Clone();
            else throw new ArgumentException("Matrix must contain one coefficient per source/destination pair.", nameof(matrix));
            foreach (float coefficient in coefficients)
                if (!float.IsFinite(coefficient)) throw new ArgumentException("Matrix coefficients must be finite.", nameof(matrix));
            if (destinationChannels == 1)
            {
                var stereo = new float[sourceChannels * 2];
                Array.Copy(coefficients, stereo, sourceChannels);
                Array.Copy(coefficients, 0, stereo, sourceChannels, sourceChannels);
                coefficients = stereo;
            }
            lock (_engine.Sync)
            {
                EnsureUsable();
                // Menu sounds set the same pan before every playback. A no-op must
                // not pause, reset and refill the native audio queue.
                if (_matrix.AsSpan().SequenceEqual(coefficients)) return;
                if (_queued.Count == 0 && _playbacks.Count == 0)
                {
                    _matrix = coefficients;
                    return;
                }
                bool running = _running;
                _running = false;
                _output.Pause();
                Pump();
                ConsumeOffset();
                ClearNativeQueue();
                foreach (var playback in _playbacks) playback.Uploaded = playback.Consumed;
                _matrix = coefficients;
                _running = running;
                Pump();
            }
        }

        private void ConsumeOffset()
        {
            int offset = Offset();
            _samplesPlayed += offset;
            foreach (var chunk in _queued)
            {
                int consumed = Math.Min(offset, chunk.Frames);
                chunk.Playback.Consumed += consumed;
                offset -= consumed;
                if (offset == 0) break;
            }
        }

        internal void PumpSafely()
        {
            try { Pump(); }
            catch (Exception error)
            {
                _failure = error;
                _running = false;
                _engine.Unregister(this);
                try { _output.Pause(); } catch { /* Keep the original failure; never escape the timer. */ }
            }
        }

        private void Pump()
        {
            int processed = _output.ProcessedBuffers;
            while (processed-- > 0)
            {
                var chunk = _queued.Dequeue();
                _output.UnqueueProcessed();
                chunk.Playback.Consumed += chunk.Frames;
                _samplesPlayed += chunk.Frames;
                if (chunk.Playback.Consumed == chunk.Playback.Length && chunk.Playback.EndOfStream) _samplesPlayed = 0;
            }
            while (_playbacks.Count > 0 && _playbacks.Peek().Consumed == _playbacks.Peek().Length)
            {
                var completed = _playbacks.Dequeue();
                if (!completed.Started) BufferStart?.Invoke(this, EventArgs.Empty);
                BufferEnd?.Invoke(this, EventArgs.Empty);
            }
            FillQueue();
            if (_running && _queued.Count > 0)
            {
                if (!_output.IsPlaying) _output.Play();
                if (_playbacks.Count > 0 && !_playbacks.Peek().Started)
                {
                    _playbacks.Peek().Started = true;
                    BufferStart?.Invoke(this, EventArgs.Empty);
                }
            }
            if (_playbacks.Count == 0) _engine.Unregister(this);
        }

        private void FillQueue()
        {
            foreach (var playback in _playbacks)
            {
                while (_queued.Count < 4 && playback.Uploaded < playback.Length)
                {
                    int frames = (int)Math.Min(Math.Max(256, _format.SamplesPerSecond / 10), playback.Length - playback.Uploaded);
                    var samples = playback.Render(playback.Uploaded, frames, _matrix);
                    _output.Submit(samples, _format.SamplesPerSecond);
                    _queued.Enqueue((playback, frames));
                    playback.Uploaded += frames;
                    AudioEngine.BuffersUploaded++;
                }
                if (_queued.Count == 4) break;
            }
        }

        private int Offset()
        {
            return _queued.Count == 0 ? 0 : _output.SampleOffset;
        }

        private void ClearNativeQueue()
        {
            _output.Clear();
            _queued.Clear();
        }

        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_failure != null) throw new InvalidOperationException(_engine.Backend.Name + " playback pump failed.", _failure);
        }

        public void DestroyVoice() => Dispose();
        public void Dispose()
        {
            lock (_engine.Sync)
            {
                if (_disposed) return;
                _engine.Unregister(this);
                _disposed = true;
                try { _output.Dispose(); }
                finally { _queued.Clear(); _playbacks.Clear(); }
            }
        }

        public bool IsDisposed => _disposed;
        public event EventHandler BufferEnd;
        public event EventHandler BufferStart;
    }

    public struct VoiceState
    {
        public long BuffersQueued;
        public long SamplesPlayed;
    }

    public struct VoiceDetails
    {
        public int ActiveFlags;
        public int InputChannels;
        public int InputSampleRate;
        public int InputProcessingStage;
    }

    public class AudioBuffer : IDisposable
    {
        public object AudioData { get; set; }
        public int AudioBytes { get; set; }
        public int FlagsValue { get; set; }
        public BufferFlags Flags { get; set; }
        public int PlayBegin { get; set; }
        public int PlayLength { get; set; }
        public int LoopBegin { get; set; }
        public int LoopLength { get; set; }
        public int LoopCount { get; set; }

        /// <summary>Decoded PCM bytes, taken from the WaveStream the game supplied.</summary>
        internal byte[] Pcm => (AudioData as SlimDX.Multimedia.WaveStream)?.Pcm;

        public void Dispose() { }
    }
}
