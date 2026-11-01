using System.Reflection;
using System.Text;
using SlimDX.Multimedia;

internal static class ShortReadChecks
{
    internal static void Run()
    {
        byte[] valid = Fixture();
        using (var source = new OneByteStream(valid))
        using (var decoded = Create(source))
        {
            var format = (WaveFormat)decoded.GetType().GetProperty("Format")!.GetValue(decoded)!;
            if (format.Channels != 2 || format.SamplesPerSecond != 44100 || decoded.Length != 4)
                throw new Exception("Short reads changed the xWMA header");
            var packets = (int[])decoded.GetType().GetProperty("DecodedPacketCumulativeBytes")!.GetValue(decoded)!;
            if (!packets.SequenceEqual(new[] { 4096 })) throw new Exception("Short reads changed dpds packet boundaries");
            byte[] payload = new byte[4];
            decoded.ReadExactly(payload);
            if (!payload.SequenceEqual(new byte[] { 1, 2, 3, 4 })) throw new Exception("Short-read xWMA data offset changed");
        }
        byte[] truncated = valid[..30];
        BitConverter.GetBytes(truncated.Length - 8).CopyTo(truncated, 4);
        Reject(truncated);
        byte[] partialTag = valid.Concat(new byte[] { (byte)'f', (byte)'m' }).ToArray();
        BitConverter.GetBytes(partialTag.Length - 8).CopyTo(partialTag, 4);
        Reject(partialTag);
        Console.WriteLine("PASS xWMA exact reads: one-byte reads, truncated fields and partial chunk header");
    }

    private static void Reject(byte[] bytes)
    {
        using var source = new OneByteStream(bytes);
        try { using var ignored = Create(source); }
        catch (TargetInvocationException error) when (error.InnerException is EndOfStreamException) { return; }
        throw new Exception("Truncated xWMA input did not fail explicitly");
    }

    private static Stream Create(Stream source) => (Stream)Activator.CreateInstance(
        typeof(Shooting.BaseObject).Assembly.GetType("Shooting.xWMAStream", true)!, source)!;

    private static byte[] Fixture()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
        {
            writer.Write("RIFF"u8); writer.Write(52); writer.Write("XWMA"u8);
            writer.Write("fmt "u8); writer.Write(16);
            writer.Write((ushort)353); writer.Write((ushort)2); writer.Write(44100); writer.Write(16000);
            writer.Write((ushort)512); writer.Write((ushort)16);
            writer.Write("dpds"u8); writer.Write(4); writer.Write(4096);
            writer.Write("data"u8); writer.Write(4); writer.Write(new byte[] { 1, 2, 3, 4 });
        }
        return stream.ToArray();
    }

    private sealed class OneByteStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 1));
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, 1)]);
    }
}
