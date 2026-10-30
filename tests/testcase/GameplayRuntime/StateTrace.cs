using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Shooting;

internal static class StateTrace
{
    private static readonly Dictionary<Type, FieldInfo[]> Fields = new();
    private static readonly HashSet<string> VisualFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "hiScore", "Angle3D", "OnRotate", "colorValue", "transparentValue", "maxTransparent",
        "<Mirrored>k__BackingField"
    };

    public static FieldInfo Field(Type type, string name) => type.GetField(name,
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(type.FullName, name);

    public static SortedDictionary<string, string> Capture(StageDataPackage stage)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["stage.TimeMain"] = stage.TimeMain.ToString(CultureInfo.InvariantCulture),
            ["stage.TimeTotal"] = stage.TimeTotal.ToString(CultureInfo.InvariantCulture),
            ["stage.SlowMode"] = stage.SlowMode.ToString(CultureInfo.InvariantCulture),
            ["rng.inext"] = Field(typeof(MyRandom), "inext").GetValue(stage.Ran)!.ToString()!,
            ["rng.inextp"] = Field(typeof(MyRandom), "inextp").GetValue(stage.Ran)!.ToString()!,
            ["rng.ranValue"] = stage.Ran.ranValue.ToString(CultureInfo.InvariantCulture)
        };
        var seeds = (int[])Field(typeof(MyRandom), "seedArray").GetValue(stage.Ran)!;
        for (int i = 0; i < seeds.Length; i++) values[$"rng.seed[{i}]"] = seeds[i].ToString(CultureInfo.InvariantCulture);
        Entity(values, "player", stage.MyPlane);
        Entity(values, "boss", stage.Boss);
        Entities(values, "bullets", stage.BulletList);
        Entities(values, "enemies", stage.EnemyPlaneList);
        Entities(values, "mybullets", stage.MyBulletList);
        Entities(values, "items", stage.ItemList);
        Entities(values, "spells", stage.SpellList);
        return values;
    }

    public static string Digest(SortedDictionary<string, string> values) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', values.Select(p => $"{p.Key}={p.Value}")))));

    public static void VerifyGameplaySensitivity()
    {
        var subject = new BaseObject();
        foreach (string name in new[] { "angle", "angleWithDirection", "scaleLength", "scaleWidth", "maxScaleL", "maxScaleW", "minScaleL", "minScaleW", "scaleX", "scaleY" })
        {
            var field = Field(typeof(BaseObject), name);
            object saved = field.GetValue(subject)!;
            var before = new SortedDictionary<string, string>(StringComparer.Ordinal); Entity(before, "subject", subject);
            object changed = saved switch { bool flag => (object)!flag, float single => (object)(single + 1f), double number => (object)(number + 1d), _ => throw new InvalidOperationException("Unexpected gameplay field type: " + name) };
            field.SetValue(subject, changed);
            var after = new SortedDictionary<string, string>(StringComparer.Ordinal); Entity(after, "subject", subject);
            if (Digest(before) == Digest(after)) throw new InvalidOperationException("Gameplay trace omits collision/motion field: " + name);
            field.SetValue(subject, saved);
        }
        Console.WriteLine("GAMEPLAY_TRACE_SENSITIVITY_PASS collision/binding/scale constraints");
    }

    private static void Entities(SortedDictionary<string, string> values, string prefix, IEnumerable list)
    {
        int index = 0;
        foreach (object entity in list) Entity(values, $"{prefix}[{index++}]", entity);
        values[$"{prefix}.count"] = index.ToString(CultureInfo.InvariantCulture);
    }

    private static void Entity(SortedDictionary<string, string> values, string prefix, object? entity)
    {
        values[$"{prefix}.type"] = entity?.GetType().FullName ?? "null";
        if (entity is null) return;
        if (!Fields.TryGetValue(entity.GetType(), out var fields))
        {
            var collected = new List<FieldInfo>();
            for (Type? type = entity.GetType(); type != null && type != typeof(object); type = type.BaseType)
                collected.AddRange(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            fields = collected.Where(f => !VisualFields.Contains(f.Name)).ToArray();
            Fields.Add(entity.GetType(), fields);
        }
        foreach (var field in fields)
            Scalar(values, $"{prefix}.{field.DeclaringType!.Name}.{field.Name}", field.GetValue(entity));
    }

    private static void Scalar(SortedDictionary<string, string> values, string key, object? value)
    {
        switch (value)
        {
            case null: return;
            case float number: values[key] = $"f32:{BitConverter.SingleToInt32Bits(number):X8}"; return;
            case double number: values[key] = $"f64:{BitConverter.DoubleToInt64Bits(number):X16}"; return;
            case System.Drawing.PointF point:
                Scalar(values, key + ".X", point.X); Scalar(values, key + ".Y", point.Y); return;
            case System.Drawing.Point point: values[key] = $"{point.X},{point.Y}"; return;
            case System.Drawing.Rectangle rectangle: values[key] = rectangle.ToString(); return;
            case string text: values[key] = text; return;
            case IDictionary dictionary:
                foreach (DictionaryEntry item in dictionary) Scalar(values, $"{key}[{item.Key}]", item.Value);
                return;
            case Array array:
                values[key + ".shape"] = string.Join(",", Enumerable.Range(0, array.Rank).Select(array.GetLength));
                int index = 0;
                foreach (object? item in array) Scalar(values, $"{key}[{index++}]", item);
                return;
        }
        if (value.GetType().IsPrimitive || value.GetType().IsEnum)
            values[key] = Convert.ToString(value, CultureInfo.InvariantCulture)!;
    }
}
