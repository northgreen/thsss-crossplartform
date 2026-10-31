using System.Text;

internal sealed record MotionCase(string Name, uint X = 0x3f800000, uint Y = 0xc0000000,
    uint Velocity = 0x40400000, ulong Direction = 0x3fe0000000000000,
    uint ScaleX = 0x3f800000, uint ScaleY = 0x3f800000, int Left = 16, int Top = 24,
    uint DestX = 0, uint DestY = 0, uint Accelerate = 0x3e800000, int Time = 17,
    bool NullStage = false, bool Override = false);

internal static class Cases
{
    private const int Magic = 0x4d4f5631;
    public static float Float(uint bits) => BitConverter.Int32BitsToSingle(unchecked((int)bits));
    public static double Double(ulong bits) => BitConverter.Int64BitsToDouble(unchecked((long)bits));

    public static void Generate(string path)
    {
        MotionCase[] cases = Enumerate().ToArray();
        using var writer = new BinaryWriter(new FileStream(path, FileMode.CreateNew), Encoding.UTF8);
        writer.Write(Magic); writer.Write(cases.Length);
        foreach (var c in cases)
        {
            writer.Write(c.Name); writer.Write(c.X); writer.Write(c.Y); writer.Write(c.Velocity);
            writer.Write(c.Direction); writer.Write(c.ScaleX); writer.Write(c.ScaleY);
            writer.Write(c.Left); writer.Write(c.Top); writer.Write(c.DestX); writer.Write(c.DestY);
            writer.Write(c.Accelerate); writer.Write(c.Time); writer.Write(c.NullStage); writer.Write(c.Override);
        }
    }

    public static MotionCase[] Read(string path)
    {
        using var r = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
        if (r.ReadInt32() != Magic) throw new InvalidDataException("Unsupported motion corpus");
        int count = r.ReadInt32();
        if (count is < 5000 or > 10000) throw new InvalidDataException("Motion case count outside bounds");
        var cases = new MotionCase[count];
        for (int i = 0; i < count; i++)
            cases[i] = new(r.ReadString(), r.ReadUInt32(), r.ReadUInt32(), r.ReadUInt32(), r.ReadUInt64(),
                r.ReadUInt32(), r.ReadUInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadUInt32(), r.ReadUInt32(),
                r.ReadUInt32(), r.ReadInt32(), r.ReadBoolean(), r.ReadBoolean());
        if (r.BaseStream.Position != r.BaseStream.Length || cases.Select(c => c.Name).Distinct().Count() != count)
            throw new InvalidDataException("Trailing input or duplicate motion keys");
        return cases;
    }

    private static IEnumerable<MotionCase> Enumerate()
    {
        yield return new("normal");
        yield return new("null-stage", NullStage: true);
        yield return new("virtual-override", Override: true);
        yield return new("virtual-null-stage", Override: true, NullStage: true);
        uint[] floats = { 0, 0x80000000, 1, 0x80000001, 0x007fffff, 0x807fffff,
            0x00800000, 0x80800000, 0x3f800000, 0xbf800000, 0x7f7fffff, 0xff7fffff,
            0x4b7fffff, 0x4b800000, 0x4b800001, 0xcb7fffff, 0xcb800000, 0xcb800001,
            0x7f800000, 0xff800000, 0x7fc00001, 0xffc12345, 0x7f800001, 0xff800123 };
        ulong[] angles = { 0, 0x8000000000000000, 1, 0x8000000000000001,
            0x000fffffffffffff, 0x800fffffffffffff, 0x0010000000000000, 0x8010000000000000,
            0x3ff0000000000000, 0xbff0000000000000, 0x400921fb54442d18,
            0x43dfffffffffffff, 0x43e0000000000000, 0x43e0000000000001,
            0xc3e0000000000001, 0x7fefffffffffffff, 0xffefffffffffffff,
            0x7ff0000000000000, 0xfff0000000000000, 0x7ff8000000000001,
            0xfff8123456789abc, 0x7ff0000000000001, 0xfff0000000000123 };
        int[] offsets = { int.MinValue, int.MinValue + 1, -16777218, -16777217, -16777216,
            -16777215, -24, -1, 0, 1, 16, 16777215, 16777216, 16777217, 16777218,
            int.MaxValue - 1, int.MaxValue };
        foreach (int offset in offsets)
        foreach (uint f in floats)
        {
            yield return new($"offset-{offset}-{f:X8}", X: f, Y: f, Left: offset, Top: offset, Velocity: 0);
            yield return new($"cancel-{offset}-{f:X8}", X: 0x3f800000, Y: 0xbf800000,
                Velocity: f, Left: offset, Top: -1);
        }
        foreach (ulong a in angles)
        foreach (uint f in floats)
        {
            yield return new($"angle-velocity-{a:X16}-{f:X8}", Direction: a, Velocity: f);
            yield return new($"angle-scale-{a:X16}-{f:X8}", Direction: a, ScaleX: f, ScaleY: f);
            yield return new($"angle-position-{a:X16}-{f:X8}", Direction: a, X: f, Y: f);
        }
        foreach (uint v in new uint[] { 0, 0x80000000, 0x3f800000, 0xbf800000, 0x41200000, 0xc1200000 })
        foreach (uint scale in new uint[] { 0, 0x3f800000 })
        foreach (uint dest in new uint[] { 0x3f800000, 0x40000000, 0x42c80000 })
            yield return new($"destination-{v:X8}-{scale:X8}-{dest:X8}", X: 0, Y: 0,
                Velocity: v, ScaleX: scale, ScaleY: scale, DestX: dest, DestY: dest);
        ulong state = 0x4d4f54494f4e3236;
        for (int i = 0; i < 4200; i++)
        {
            uint x = (uint)Next(ref state), y = (uint)Next(ref state), v = (uint)Next(ref state);
            ulong a = Next(ref state);
            uint sx = (uint)Next(ref state), sy = (uint)Next(ref state);
            int left = unchecked((int)Next(ref state)), top = unchecked((int)Next(ref state));
            // Half the random corpus follows normal finite game ranges; half retains arbitrary IEEE payloads.
            if ((i & 1) == 0)
            {
                x = Bits((int)(x % 640)); y = Bits((int)(y % 480)); v = Bits((int)(v % 100) - 20);
                a = unchecked((ulong)BitConverter.DoubleToInt64Bits((long)(a % 62832) / 10000.0));
                sx = Bits((sx % 9) / 4f); sy = Bits((sy % 9) / 4f); left = 16; top = 24;
            }
            yield return new($"random-{i:D4}", x, y, v, a, sx, sy, left, top);
        }
    }

    private static uint Bits(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value));
    private static ulong Next(ref ulong state)
    {
        state ^= state << 13; state ^= state >> 7; state ^= state << 17;
        return state;
    }
}
