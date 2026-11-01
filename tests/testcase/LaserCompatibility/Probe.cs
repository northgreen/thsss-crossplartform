using System.Drawing;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;

if (args.Length != 5) throw new ArgumentException("Expected DLL, backend, precision, inputs, outputs");
string dll = Path.GetFullPath(args[0]);
string directory = Path.GetDirectoryName(dll)!;
AssemblyLoadContext.Default.Resolving += (context, name) =>
    File.Exists(Path.Combine(directory, name.Name + ".dll"))
        ? context.LoadFromAssemblyPath(Path.Combine(directory, name.Name + ".dll")) : null;
Assembly game = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
NativeLibrary.SetDllImportResolver(game, (name, _, _) => name == "thsss_legacy_math"
    ? NativeLibrary.Load(Path.Combine(directory, "libthsss_legacy_math.so")) : IntPtr.Zero);
Type legacy = game.GetType("SlimDX.Compat.LegacyFloat", true)!;
int precision = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
legacy.GetProperty("Precision")!.SetValue(null, precision);
if ((string)legacy.GetProperty("Backend")!.GetValue(null)! != args[1] ||
    (int)legacy.GetProperty("Precision")!.GetValue(null)! != precision)
    throw new InvalidOperationException("Backend/precision selection differs");
Type baseType = game.GetType("Shooting.BaseObject", true)!;
Type laserType = game.GetType("Shooting.Bullet_StraightLaser", true)!;
Type derivedType = MakeHiddenTarget(baseType);
MethodInfo dispatch = baseType.GetMethod("HitCheck", new[] { baseType, typeof(float) })!;
if (!dispatch.IsVirtual || laserType.GetMethod("HitCheck", new[] { baseType, typeof(float) })!.DeclaringType != laserType)
    throw new InvalidOperationException("Expected actual StraightLaser virtual override");
foreach (string property in new[] { "OriginalPosition", "Region", "Scale", "LastPoints" })
    if (baseType.GetProperty(property)!.GetMethod!.IsVirtual)
        throw new InvalidOperationException("Frozen nonvirtual property contract changed: " + property);
using var input = new BinaryReader(File.OpenRead(args[3]));
using var output = new BinaryWriter(new FileStream(args[4], FileMode.CreateNew));
if (input.ReadInt32() != 0x31525a4c) throw new InvalidDataException("Invalid laser corpus");
int count = input.ReadInt32();
output.Write(0x31525a4c);
output.Write(count);
for (int index = 0; index < count; index++) Run(index);
if (input.BaseStream.Position != input.BaseStream.Length) throw new InvalidDataException("Trailing corpus bytes");

void Set(object instance, string name, object? value) => baseType.GetProperty(name)!.SetValue(instance, value);
float ReadFloat() => BitConverter.Int32BitsToSingle(input.ReadInt32());
void Run(int index)
{
    int length = input.ReadInt32();
    byte targetKind = input.ReadByte();
    int region = input.ReadInt32();
    float scale = ReadFloat(), scaleLength = ReadFloat(), radius = ReadFloat();
    var position = new PointF(ReadFloat(), ReadFloat());
    sbyte expected = input.ReadSByte();
    PointF[]? points = length < 0 ? null : new PointF[length];
    for (int i = 0; i < length; i++) points![i] = new PointF(ReadFloat(), ReadFloat());
    object laser = RuntimeHelpers.GetUninitializedObject(laserType);
    object? target = targetKind == 0 ? null : RuntimeHelpers.GetUninitializedObject(targetKind == 2 ? derivedType : baseType);
    foreach (object instance in target == null ? new[] { laser } : new[] { laser, target })
    {
        Set(instance, "MaxScaleL", float.PositiveInfinity);
        Set(instance, "MinScaleL", float.NegativeInfinity);
        Set(instance, "MaxScaleW", float.PositiveInfinity);
        Set(instance, "MinScaleW", float.NegativeInfinity);
    }
    Set(laser, "LastPoints", points);
    Set(laser, "ScaleLength", scaleLength);
    if (target != null)
    {
        Set(target, "OriginalPosition", position);
        Set(target, "Region", region);
        Set(target, "Scale", scale);
    }
    string before = State(laser) + (target == null ? "null" : State(target));
    string result;
    try { result = (bool)dispatch.Invoke(laser, new object?[] { target, radius })! ? "true" : "false"; }
    catch (TargetInvocationException error) when (error.InnerException != null) { result = error.InnerException.GetType().FullName!; }
    string after = State(laser) + (target == null ? "null" : State(target));
    if (before != after) throw new InvalidOperationException($"Observable mutation at {index}");
    string? literal = expected switch { 0 => "false", 1 => "true", 2 => "System.NullReferenceException", _ => null };
    if (literal != null && result != literal) throw new InvalidOperationException($"Literal {index}: expected {literal}, got {result}");
    output.Write(index);
    output.Write(result);
    output.Write(before);
}

static Type MakeHiddenTarget(Type baseType)
{
    var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("LaserHiddenTarget"), AssemblyBuilderAccess.Run).DefineDynamicModule("Main");
    var type = module.DefineType("HiddenTarget", TypeAttributes.Public, baseType);
    foreach (string name in new[] { "OriginalPosition", "Region", "Scale" })
    {
        Type valueType = baseType.GetProperty(name)!.PropertyType;
        var getter = type.DefineMethod("get_" + name, MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, valueType, Type.EmptyTypes);
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Throw);
        type.DefineProperty(name, PropertyAttributes.None, valueType, null).SetGetMethod(getter);
    }
    return type.CreateType()!;
}

static string State(object instance)
{
    var text = new StringBuilder();
    for (Type? type = instance.GetType(); type != null; type = type.BaseType)
    foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).OrderBy(f => f.Name, StringComparer.Ordinal))
    {
        object? value = field.GetValue(instance);
        text.Append(type.FullName).Append('.').Append(field.Name).Append('=');
        text.Append(value switch
        {
            null => "null", float f => BitConverter.SingleToInt32Bits(f).ToString("X8"),
            double d => BitConverter.DoubleToInt64Bits(d).ToString("X16"),
            PointF p => PointBits(p), PointF[] array => string.Join(';', array.Select(PointBits)),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        }).Append('|');
    }
    return text.ToString();
}
static string PointBits(PointF p) => $"{BitConverter.SingleToInt32Bits(p.X):X8},{BitConverter.SingleToInt32Bits(p.Y):X8}";
