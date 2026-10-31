using System.Text;

internal sealed record CollisionCase(string Name, float X, float Y, float TargetX, float TargetY,
    float Radius = 3, float Length = 1, float Width = 1, float TargetScale = 1,
    int TargetRegion = 3, int Time = 9, int Region = 3, bool NullTarget = false,
    double Angle = 0, double Direction = 0, bool AngleWithDirection = true, int Dispatch = 0);

internal static class Cases
{
    private const int Magic = 0x434F4C31;

    public static void Generate(string path)
    {
        CollisionCase[] cases = Enumerate().ToArray();
        using var writer = new BinaryWriter(new FileStream(path, FileMode.CreateNew), Encoding.UTF8);
        writer.Write(Magic);
        writer.Write(cases.Length);
        foreach (var c in cases)
        {
            writer.Write(c.Name);
            foreach (float f in new[] { c.X, c.Y, c.TargetX, c.TargetY, c.Radius, c.Length, c.Width, c.TargetScale })
                writer.Write(BitConverter.SingleToInt32Bits(f));
            writer.Write(c.TargetRegion); writer.Write(c.Time); writer.Write(c.Region);
            writer.Write(c.NullTarget); writer.Write(c.Angle); writer.Write(c.Direction);
            writer.Write(c.AngleWithDirection); writer.Write(c.Dispatch);
        }
    }

    public static CollisionCase[] Read(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
        if (reader.ReadInt32() != Magic) throw new InvalidDataException("Unsupported collision input format");
        int count = reader.ReadInt32();
        if (count is < 1 or > 1000000) throw new InvalidDataException("Invalid collision case count");
        var cases = new CollisionCase[count];
        for (int i = 0; i < count; i++)
        {
            string name = reader.ReadString();
            var f = new float[8];
            for (int j = 0; j < f.Length; j++) f[j] = BitConverter.Int32BitsToSingle(reader.ReadInt32());
            cases[i] = new(name, f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7],
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadBoolean(),
                reader.ReadDouble(), reader.ReadDouble(), reader.ReadBoolean(), reader.ReadInt32());
        }
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Trailing input data");
        return cases;
    }

    private static IEnumerable<CollisionCase> Enumerate()
    {
        yield return new("literal-3-4-5", 0, 0, 3, 4);
        yield return new("literal-coincident", 10, -20, 10, -20);
        foreach (int time in new[] { 8, 9 })
        foreach (bool nullTarget in new[] { false, true })
        foreach (bool ellipse in new[] { false, true })
            yield return new($"guard-{time}-{nullTarget}-{ellipse}", 0, 0, 1, 2,
                Time: time, NullTarget: nullTarget, Width: ellipse ? 2 : 1);

        int[] regions = { int.MinValue, int.MinValue + 1, -16777217, -16777216, -16777215,
            -1, 0, 1, 3, 16777215, 16777216, 16777217, 16777218, int.MaxValue - 1, int.MaxValue };
        float[] specials = { 0, Float(0x80000000), Float(1), Float(0x80000001), Float(0x007fffff),
            Float(0x00800000), -1, 1, -3, 3, float.MaxValue, -float.MaxValue,
            float.PositiveInfinity, float.NegativeInfinity, Float(0x7fc00001), Float(0xffc12345), Float(0x7f800001) };
        foreach (int region in regions)
        foreach (float scale in specials)
        {
            string id = $"region-{region}-scale-{Bits(scale)}";
            yield return new(id, 0, 0, 6, 0, TargetRegion: region, TargetScale: scale, Region: region);
        }
        foreach (float f in specials)
        {
            string id = Bits(f);
            yield return new($"coordinate-x-{id}", f, 0, 1, 2);
            yield return new($"coordinate-y-{id}", 0, f, 1, 2);
            yield return new($"target-x-{id}", 1, 2, f, 0);
            yield return new($"target-y-{id}", 1, 2, 0, f);
            foreach (float g in specials)
            {
                yield return new($"radius-scale-{id}-{Bits(g)}", 0, 0, 1, 2, Radius: f, Length: g, Width: g);
                yield return new($"ellipse-scale-{id}-{Bits(g)}", 0, 0, 1, 2, Length: f, Width: g);
            }
        }
        foreach (float radius in new[] { -100f, -1f, 0f, 1f, 3f, 100f, 16777216f })
        foreach (int region in regions)
        {
            // These are input neighborhoods, not a computed collision oracle.
            float contact = radius - region;
            foreach (float x in new[] { MathF.BitDecrement(contact), contact, MathF.BitIncrement(contact) })
                yield return new($"contact-{Bits(radius)}-{region}-{Bits(x)}", 0, 0, x, 0,
                    Radius: radius, TargetRegion: region);
        }
        double[] angles = { 0, Math.PI / 4, Math.PI / 2, Math.PI, -Math.PI, 1e20, double.NaN, double.PositiveInfinity };
        foreach (double angle in angles)
        foreach (bool followsDirection in new[] { false, true })
        foreach (int time in new[] { 8, 9 })
            yield return new($"ellipse-angle-{BitConverter.DoubleToInt64Bits(angle):X16}-{followsDirection}-{time}",
                2, -3, 5, 7, Length: 2, Width: 0.5f, Angle: angle, Direction: Math.PI / 3,
                AngleWithDirection: followsDirection, Time: time);
        foreach (int dispatch in new[] { 1, 2 })
        foreach (int time in new[] { 8, 9 })
        foreach (bool nullTarget in new[] { false, true })
            yield return new($"dispatch-{dispatch}-{time}-{nullTarget}", 0, 0, 0, 0,
                Dispatch: dispatch, Time: time, NullTarget: nullTarget);

        uint state = 0xC0111510;
        for (int i = 0; i < 4096; i++)
        {
            float x = Finite(ref state), y = Finite(ref state), tx = Finite(ref state), ty = Finite(ref state);
            float radius = Finite(ref state), scale = Finite(ref state), targetScale = Finite(ref state);
            int region = unchecked((int)Next(ref state));
            yield return new($"random-bits-{i:D5}", x, y, tx, ty, radius, scale, scale, targetScale, region);
            float ordinaryX = (int)(Next(ref state) % 200001) / 100f - 1000;
            float ordinaryY = (int)(Next(ref state) % 200001) / 100f - 1000;
            yield return new($"random-ordinary-{i:D5}", ordinaryX, ordinaryY, ordinaryX + (int)(Next(ref state) % 200) - 100,
                ordinaryY + (int)(Next(ref state) % 200) - 100, Next(ref state) % 100,
                Length: 0.5f, Width: 0.5f, TargetScale: 0.75f, TargetRegion: (int)(Next(ref state) % 100));
        }
    }

    private static string Bits(float value) => BitConverter.SingleToInt32Bits(value).ToString("X8");
    private static float Float(uint bits) => BitConverter.Int32BitsToSingle(unchecked((int)bits));
    private static uint Next(ref uint state) { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state; }
    private static float Finite(ref uint state)
    {
        uint bits;
        do { bits = Next(ref state); } while ((bits & 0x7f800000) == 0x7f800000);
        return Float(bits);
    }
}
