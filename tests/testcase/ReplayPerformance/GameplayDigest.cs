using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Shooting;

// Optional correctness tracing, measured separately from timing-only captures.
internal static class GameplayDigest
{
    private static readonly Dictionary<Type, byte[]> TypeNames = new();
    private static readonly FieldInfo Seed = typeof(MyRandom).GetField("seedArray", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo Next = typeof(MyRandom).GetField("inext", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo NextP = typeof(MyRandom).GetField("inextp", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static string Capture(StageDataPackage stage)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> data = stackalloc byte[128];
        int at = 0;
        Put(data, ref at, stage.TimeMain); Put(data, ref at, stage.TimeTotal); Put(data, ref at, stage.SlowMode);
        Put(data, ref at, (int)Next.GetValue(stage.Ran)!); Put(data, ref at, (int)NextP.GetValue(stage.Ran)!);
        Put(data, ref at, stage.Ran.ranValue); hash.AppendData(data[..at]);
        foreach (int value in (int[])Seed.GetValue(stage.Ran)!) { at = 0; Put(data, ref at, value); hash.AppendData(data[..at]); }
        Entity(hash, stage.MyPlane); Entity(hash, stage.Boss);
        foreach (var list in new System.Collections.IEnumerable[] { stage.BulletList, stage.EnemyPlaneList, stage.MyBulletList, stage.ItemList, stage.SpellList })
        {
            int count = 0;
            foreach (BaseObject? entity in list) { Entity(hash, entity); count++; }
            at = 0; Put(data, ref at, count); hash.AppendData(data[..at]);
        }
        var player = stage.MyPlane; at = 0;
        BinaryPrimitives.WriteInt64LittleEndian(data, player.Score); at += 8;
        Put(data, ref at, player.Life); Put(data, ref at, player.Spell); Put(data, ref at, player.Power);
        Put(data, ref at, player.Graze); Put(data, ref at, player.StarPoint); Put(data, ref at, player.HighItemScore);
        Put(data, ref at, BitConverter.SingleToInt32Bits(player.Rate)); hash.AppendData(data[..at]);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Entity(IncrementalHash hash, BaseObject? entity)
    {
        if (entity is null) { hash.AppendData(new byte[] { 0 }); return; }
        Type type = entity.GetType();
        if (!TypeNames.TryGetValue(type, out byte[]? name)) TypeNames[type] = name = Encoding.UTF8.GetBytes(type.FullName! + "\0");
        hash.AppendData(name);
        Span<byte> data = stackalloc byte[128]; int at = 0;
        Put(data, ref at, entity.Time); Put(data, ref at, BitConverter.SingleToInt32Bits(entity.OriginalPosition.X));
        Put(data, ref at, BitConverter.SingleToInt32Bits(entity.OriginalPosition.Y)); Put(data, ref at, BitConverter.SingleToInt32Bits(entity.Velocity));
        BinaryPrimitives.WriteInt64LittleEndian(data[at..], BitConverter.DoubleToInt64Bits(entity.Direction)); at += 8;
        BinaryPrimitives.WriteInt64LittleEndian(data[at..], BitConverter.DoubleToInt64Bits(entity.Angle)); at += 8;
        Put(data, ref at, BitConverter.SingleToInt32Bits(entity.ScaleWidth)); Put(data, ref at, BitConverter.SingleToInt32Bits(entity.ScaleLength));
        Put(data, ref at, entity.Region); Put(data, ref at, entity.Damage); Put(data, ref at, BitConverter.SingleToInt32Bits(entity.HealthPoint));
        Put(data, ref at, entity.HitEnabled ? 1 : 0); Put(data, ref at, entity.Enabled ? 1 : 0); Put(data, ref at, entity.Active ? 1 : 0);
        hash.AppendData(data[..at]);
    }

    private static void Put(Span<byte> data, ref int at, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data[at..], value); at += 4;
    }

    private static void Put(Span<byte> data, ref int at, long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(data[at..], value); at += 8;
    }
}
