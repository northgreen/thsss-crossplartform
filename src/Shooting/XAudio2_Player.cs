using System.IO;
using SlimDX.Multimedia;
using SlimDX.XAudio2;

namespace Shooting;

public class XAudio2_Player
{
	private XAudio2 device;

	private SourceVoice sourceVoice;

	private AudioBuffer buffer;

	public float VoicePos { get; set; }

	public bool loadSuccess { get; private set; }

	public long Position => sourceVoice.State.SamplesPlayed / 44;

	public int Volume
	{
		get
		{
			return (int)(sourceVoice.Volume * 200f);
		}
		set
		{
			if (0 <= value && value <= 100)
			{
				sourceVoice.Volume = (float)value / 200f;
			}
		}
	}

	public XAudio2_Player(string fileName, XAudio2 device)
	{
		this.device = device;
		loadSuccess = (LoadSound(fileName) ? true : false);
	}

	private bool LoadSound(string fileName)
	{
		try
		{
			FileStream fileStream = File.OpenRead(fileName);
			WaveStream waveStream = new WaveStream(fileStream);
			fileStream.Close();
			buffer = new AudioBuffer();
			buffer.AudioData = waveStream;
			buffer.AudioBytes = (int)waveStream.Length;
			buffer.Flags = BufferFlags.EndOfStream;
			sourceVoice = new SourceVoice(device, waveStream.Format);
			Volume = 40;
			return true;
		}
		catch
		{
			return false;
		}
	}

	public void Play()
	{
		if (loadSuccess)
		{
			float num = VoicePos * 0.4f + 0.3f;
			int inputChannels = sourceVoice.VoiceDetails.InputChannels;
			float[] matrix = new float[2]
			{
				1f - num,
				num
			};
			if (inputChannels == 2)
			{
				matrix = new float[3]
				{
					0f,
					1f - num,
					num
				};
			}
			sourceVoice.Stop();
			sourceVoice.FlushSourceBuffers();
			sourceVoice.SetOutputMatrix(inputChannels, 2, matrix);
			sourceVoice.SubmitSourceBuffer(buffer);
			sourceVoice.Start(PlayFlags.None);
		}
	}

	public void Play(int Volume)
	{
		int volume = this.Volume;
		this.Volume = Volume;
		Play();
		this.Volume = volume;
	}

	public void Resume()
	{
		if (loadSuccess)
		{
			sourceVoice.Start(PlayFlags.None);
		}
	}

	public void Stop()
	{
		if (!loadSuccess)
		{
			return;
		}
		try
		{
			sourceVoice.Stop();
		}
		catch
		{
		}
	}

	public void SetLoop(int LoopBegin, int LoopEnd)
	{
		SetLoop(0, 0, 255, LoopBegin, LoopEnd);
	}

	public void SetLoop(int PlayBegin, int PlayLength, int LoopCount, int LoopBegin, int LoopEnd)
	{
		buffer.PlayBegin = PlayBegin;
		buffer.PlayLength = PlayLength;
		buffer.LoopCount = LoopCount;
		buffer.LoopBegin = LoopBegin;
		if (LoopEnd > LoopBegin)
		{
			buffer.LoopLength = LoopEnd - LoopBegin;
		}
	}

	public void Dispose()
	{
		if (loadSuccess)
		{
			Stop();
			sourceVoice.Dispose();
			buffer.Dispose();
		}
	}
}
