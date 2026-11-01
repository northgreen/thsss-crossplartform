using System;
using System.IO;
using System.Text;
using SlimDX.Multimedia;

namespace Shooting;

internal class xWMAStream : Stream
{
	public const ushort WAVE_FORMAT_xWMA = 353;

	public const ushort WAVE_FORMAT_EXTENSIBLE = 65534;

	protected Stream dataStream;

	protected long dataOffset;

	protected int dataSize;

	protected int[] decodedPacketCumulativeBytes;

	protected WaveFormat format;

	public int[] DecodedPacketCumulativeBytes => decodedPacketCumulativeBytes;

	public WaveFormat Format => format;

	public override bool CanRead => true;

	public override bool CanWrite => false;

	public override bool CanSeek => true;

	public override long Position
	{
		get
		{
			return dataStream.Position - dataOffset;
		}
		set
		{
			Seek(value, SeekOrigin.Begin);
		}
	}

	public override long Length => dataSize;

	public xWMAStream(Stream stream)
	{
		dataStream = stream;
		analyzeXWMAFileStream();
	}

	public xWMAStream(string path)
	{
		if (!File.Exists(path))
		{
			throw new FileNotFoundException(path + " Not Found");
		}
		dataStream = new FileStream(path, FileMode.Open);
		analyzeXWMAFileStream();
	}

	private void analyzeXWMAFileStream()
	{
		byte[] array = new byte[4];
		dataStream.Seek(0L, SeekOrigin.Begin);
		dataStream.ReadExactly(array, 0, 4);
		if (Encoding.ASCII.GetString(array) != "RIFF")
		{
			throw new InvalidDataException("Invalid xWMA file.");
		}
		dataStream.ReadExactly(array, 0, 4);
		int num = BitConverter.ToInt32(array, 0);
		if (dataStream.Length != num + 8)
		{
			throw new InvalidDataException("Invalid Format");
		}
		dataStream.ReadExactly(array, 0, 4);
		if (Encoding.ASCII.GetString(array) != "XWMA")
		{
			throw new InvalidDataException("Invalid xWMA file.");
		}
		while (true)
		{
			// A stream may return fewer bytes than requested without reaching EOF.
			int firstByte = dataStream.ReadByte();
			if (firstByte < 0) break;
			array[0] = (byte)firstByte;
			dataStream.ReadExactly(array, 1, 3);
			string text = Encoding.ASCII.GetString(array);
			dataStream.ReadExactly(array, 0, 4);
			num = BitConverter.ToInt32(array, 0);
			switch (text)
			{
			case "fmt ":
			{
				dataStream.ReadExactly(array, 0, 2);
				ushort num4 = BitConverter.ToUInt16(array, 0);
				format = new WaveFormat();
				dataStream.ReadExactly(array, 0, 2);
				format.Channels = (short)BitConverter.ToUInt16(array, 0);
				dataStream.ReadExactly(array, 0, 4);
				format.SamplesPerSecond = BitConverter.ToInt32(array, 0);
				dataStream.ReadExactly(array, 0, 4);
				format.AverageBytesPerSecond = BitConverter.ToInt32(array, 0);
				dataStream.ReadExactly(array, 0, 2);
				format.BlockAlignment = BitConverter.ToInt16(array, 0);
				dataStream.ReadExactly(array, 0, 2);
				format.BitsPerSample = BitConverter.ToInt16(array, 0);
				switch (num4)
				{
				case 353:
					dataStream.Seek(num - 16, SeekOrigin.Current);
					format.FormatTag = (short)WaveFormatTag.WMAudio2;
					break;
				case 65534:
				{
					WaveFormatExtensible waveFormatExtensible = new WaveFormatExtensible();
					waveFormatExtensible.Channels = format.Channels;
					waveFormatExtensible.SamplesPerSecond = format.SamplesPerSecond;
					waveFormatExtensible.AverageBytesPerSecond = format.AverageBytesPerSecond;
					waveFormatExtensible.BlockAlignment = format.BlockAlignment;
					waveFormatExtensible.BitsPerSample = format.BitsPerSample;
					dataStream.ReadExactly(array, 0, 2);
					num = BitConverter.ToInt16(array, 0);
					if (num != 22)
					{
						throw new InvalidDataException("Invalid Format.");
					}
					dataStream.ReadExactly(array, 0, 2);
					waveFormatExtensible.ValidBitsPerSample = BitConverter.ToInt16(array, 0);
					dataStream.ReadExactly(array, 0, 4);
					waveFormatExtensible.SpeakerMask = (Speakers)BitConverter.ToUInt32(array, 0);
					byte[] array2 = new byte[16];
					dataStream.ReadExactly(array2, 0, 16);
					waveFormatExtensible.SubFormat = new Guid(array2);
					waveFormatExtensible.FormatTag = (short)WaveFormatTag.Extensible;
					format = waveFormatExtensible;
					break;
				}
				default:
					throw new InvalidDataException("Invalid xWMA file.");
				}
				break;
			}
			case "dpds":
			{
				if (num % 4 != 0)
				{
					throw new InvalidDataException("Invalid Format.");
				}
				int num3 = num / 4;
				decodedPacketCumulativeBytes = new int[num3];
				for (int i = 0; i < num3; i++)
				{
					dataStream.ReadExactly(array, 0, 4);
					decodedPacketCumulativeBytes[i] = BitConverter.ToInt32(array, 0);
				}
				break;
			}
			case "data":
				dataOffset = dataStream.Position;
				dataSize = num;
				dataStream.Seek(num, SeekOrigin.Current);
				break;
			default:
				dataStream.Seek(num, SeekOrigin.Current);
				break;
			}
		}
		dataStream.Seek(dataOffset, SeekOrigin.Begin);
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		return dataStream.Read(buffer, offset, count);
	}

	public override void Write(byte[] buffer, int offset, int count)
	{
		throw new NotSupportedException("xWMA Stream objects cannot be written to.");
	}

	public override void SetLength(long value)
	{
		throw new NotSupportedException("xWMA Stream objects cannot be resized.");
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		long offset2 = 0L;
		switch (origin)
		{
		case SeekOrigin.Begin:
			offset2 = dataOffset + offset;
			break;
		case SeekOrigin.Current:
			offset2 = offset;
			break;
		case SeekOrigin.End:
			offset2 = dataOffset;
			break;
		}
		return dataStream.Seek(offset2, origin) - dataOffset;
	}

	public override void Flush()
	{
		throw new NotSupportedException("xWMA Stream objects cannot be flushed.");
	}

	public override int ReadByte()
	{
		if (dataStream.Position > dataOffset + dataSize)
		{
			return -1;
		}
		return dataStream.ReadByte();
	}

	public override void WriteByte(byte value)
	{
		throw new NotSupportedException("xWMA Stream objects cannot be written to.");
	}

	public override void Close()
	{
		dataStream.Close();
	}

	protected override void Dispose(bool disposing)
	{
		dataStream.Dispose();
	}
    public static implicit operator IntPtr(xWMAStream s) => IntPtr.Zero;
}
