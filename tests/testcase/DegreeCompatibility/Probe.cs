using System.Collections;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;

if (args.Length != 6) throw new ArgumentException("Expected DLL, native library, backend, precision, inputs, outputs");
string dll = Path.GetFullPath(args[0]);
string directory = Path.GetDirectoryName(dll)!;
AssemblyLoadContext.Default.Resolving += (context, name) =>
    File.Exists(Path.Combine(directory, name.Name + ".dll"))
        ? context.LoadFromAssemblyPath(Path.Combine(directory, name.Name + ".dll")) : null;
Assembly game = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
NativeLibrary.SetDllImportResolver(game, (name, _, _) => name == "thsss_legacy_math"
    ? NativeLibrary.Load(Path.GetFullPath(args[1])) : IntPtr.Zero);
Type legacy = game.GetType("SlimDX.Compat.LegacyFloat", true)!;
int precision = int.Parse(args[3], CultureInfo.InvariantCulture);
legacy.GetProperty("Precision")!.SetValue(null, precision);
if ((string)legacy.GetProperty("Backend")!.GetValue(null)! != args[2] ||
    (int)legacy.GetProperty("Precision")!.GetValue(null)! != precision)
    throw new InvalidOperationException("Backend/precision selection differs");
Type baseType = game.GetType("Shooting.BaseObject", true)!;
MethodInfo ctrl = baseType.GetMethod("Ctrl", Type.EmptyTypes)!;
if (!ctrl.IsVirtual || ctrl.DeclaringType != baseType)
    throw new InvalidOperationException("Expected actual BaseObject.Ctrl virtual method");
foreach (string property in new[] { "Angle", "Direction", "AngleDegree", "DirectionDegree", "AngularVelocityDegree", "DirectionVelocityDegree" })
    if (baseType.GetProperty(property)!.GetMethod!.IsVirtual)
        throw new InvalidOperationException("Nonvirtual angular accessor contract changed: " + property);
Type derived = MakeMovingProbe(baseType);
FieldInfo[] fields = baseType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
    .OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
using var input = new BinaryReader(File.OpenRead(args[4]));
using var output = new StreamWriter(new FileStream(args[5], FileMode.CreateNew), new UTF8Encoding(false)) { NewLine = "\n" };
if (input.ReadInt32() != 0x31474544) throw new InvalidDataException("Invalid degree corpus");
int count = input.ReadInt32();
if (count != 6000) throw new InvalidDataException("Expected 6000 frozen cases");
int zeroRoundtripWitnesses = 0;
for (int index = 0; index < count; index++) Run(index);
if (input.BaseStream.Position != input.BaseStream.Length) throw new InvalidDataException("Trailing input bytes");
if (zeroRoundtripWitnesses == 0) throw new InvalidOperationException("Missing nonidentity zero-rate roundtrip witness");
output.WriteLine($"witnesses={zeroRoundtripWitnesses}");

object? Get(object instance, string name) => baseType.GetProperty(name)!.GetValue(instance);
void Set(object instance, string name, object? value) => baseType.GetProperty(name)!.SetValue(instance, value);
void Raw(object instance, string name, object value) => baseType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(instance, value);
string State(object instance) => string.Join('|', fields.Select(f => f.Name + ":" + f.FieldType.FullName + "=" + Bits(f.GetValue(instance))));
void Run(int index)
{
    double angle = BitConverter.Int64BitsToDouble(input.ReadInt64());
    double direction = BitConverter.Int64BitsToDouble(input.ReadInt64());
    float rate = BitConverter.Int32BitsToSingle(input.ReadInt32());
    float directionRate = BitConverter.Int32BitsToSingle(input.ReadInt32());
    byte flags = input.ReadByte();
    if ((flags & ~3) != 0) throw new InvalidDataException("Unknown corpus flags");
    // Infinities/huge finite normalization can loop forever; those belong to pre-setter native gates.
    foreach (double value in new[] { angle, direction })
        if (double.IsInfinity(value) || (double.IsFinite(value) && Math.Abs(value) > 7))
            throw new InvalidDataException("Unsafe old-method angle");
    foreach (float value in new[] { rate, directionRate })
        if (float.IsInfinity(value) || (float.IsFinite(value) && Math.Abs(value) > 7200))
            throw new InvalidDataException("Unsafe old-method rate");
    object instance = Activator.CreateInstance(derived)!;
    Raw(instance, "angle", angle);
    Raw(instance, "direction", direction);
    Set(instance, "AngularVelocityDegree", rate);
    Set(instance, "DirectionVelocityDegree", directionRate);
    Set(instance, "Enabled", (flags & 1) == 0);
    Set(instance, "OriginalPosition", new PointF(index % 19 - 9, index % 23 - 11));
    Set(instance, "LastPoints", new[] { new PointF(17, 19), new PointF(-23, 29), new PointF(31, -37) });
    Set(instance, "MaxVelocity", 10f); Set(instance, "MinVelocity", -10f);
    Set(instance, "Velocity", index % 3 == 0 ? 9f : -9f);
    Set(instance, "Accelerate", index % 3 == 0 ? 4f : -4f);
    Set(instance, "MaxTransparent", 200);
    Set(instance, "TransparentValueF", index % 2 == 0 ? 199f : 1f);
    Set(instance, "TransparentVelocity", index % 2 == 0 ? 5f : -5f);
    Set(instance, "MinScaleL", 0.5f); Set(instance, "MaxScaleL", 2f);
    Set(instance, "MinScaleW", 0.25f); Set(instance, "MaxScaleW", 3f);
    Set(instance, "ScaleLength", 1f); Set(instance, "ScaleWidth", 1f);
    Set(instance, "ScaleVL", index % 2 == 0 ? 4f : -4f);
    Set(instance, "ScaleVW", index % 2 == 0 ? -4f : 4f);
    if ((flags & 2) != 0) FillDictionaries(instance);
    // Woven setters/getters may quiet signaling NaNs before Ctrl; compare the observed pre-state.
    string initialAngularRate = Bits(Get(instance, "AngularVelocityDegree"));
    string initialDirectionRate = Bits(Get(instance, "DirectionVelocityDegree"));
    var events = new List<string>();
    string before = "";
    ProbeHooks.OnMove = moved =>
    {
        if (!ReferenceEquals(instance, moved) || (int)Get(instance, "Time")! != 1 ||
            Bits(baseType.GetField("angle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)) != Bits(angle) ||
            Bits(baseType.GetField("direction", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)) != Bits(direction) ||
            Bits(Get(instance, "AngularVelocityDegree")) != initialAngularRate ||
            Bits(Get(instance, "DirectionVelocityDegree")) != initialDirectionRate)
            throw new InvalidOperationException("Move must precede angular increments after Time++");
        events.Add("Move:" + State(instance));
    };
    Action<string> callback = argument =>
    {
        if (argument != null || events.Count != 1) throw new InvalidOperationException("CtrlCall ordering/argument changed");
        if (((PointF[])Get(instance, "LastPoints")!)[0] != new PointF(17, 19))
            throw new InvalidOperationException("Ghost history must follow CtrlCall");
        events.Add("CtrlCall:" + State(instance));
    };
    FieldInfo call = baseType.GetField("CtrlCall")!;
    call.SetValue(instance, Delegate.CreateDelegate(call.FieldType, callback.Target, callback.Method));
    before = State(instance);
    ctrl.Invoke(instance, null);
    string after = State(instance);
    bool enabled = (flags & 1) == 0;
    if (events.Count != (enabled ? 2 : 0) || (int)Get(instance, "Time")! != (enabled ? 1 : 0))
        throw new InvalidOperationException("Ctrl guard or virtual Move/CtrlCall count changed");
    if (!enabled && before != after) throw new InvalidOperationException("Disabled Ctrl mutated state");
    if (enabled)
    {
        bool dictionaries = (flags & 2) != 0;
        if ((float)Get(instance, "Velocity")! != (dictionaries ? 7f : index % 3 == 0 ? 10f : -10f) ||
            (float)Get(instance, "TransparentValueF")! != (index % 2 == 0 ? 200f : 0f) ||
            (float)Get(instance, "ScaleLength")! != (index % 2 == 0 ? 2f : 0.5f) ||
            (float)Get(instance, "ScaleWidth")! != (index % 2 == 0 ? 0.25f : 3f))
            throw new InvalidOperationException("Nonangular common-update clamp changed");
        if (dictionaries && ((float)Get(instance, "Accelerate")! != 0.125f ||
            (float)Get(instance, "DirectionVelocityDegree")! != -90f ||
            (float)Get(instance, "TransparentVelocity")! != -0.5f ||
            (float)Get(instance, "ScaleVW")! != 0.25f || (float)Get(instance, "ScaleVL")! != -0.25f ||
            (PointF)Get(instance, "DestPoint")! != new PointF(41, -43)))
            throw new InvalidOperationException("Time-1 dictionary override changed");
        PointF[] points = (PointF[])Get(instance, "LastPoints")!;
        if (points[0] != (PointF)Get(instance, "OriginalPosition")! || points[1] != new PointF(17, 19) || points[2] != new PointF(-23, 29))
            throw new InvalidOperationException("Ghost history order changed");
        if ((flags & 2) == 0 && rate == 0 && double.IsFinite(angle) && Bits(Get(instance, "Angle")) != Bits(angle))
            zeroRoundtripWitnesses++;
    }
    output.WriteLine($"{index}\tbefore:{before}\t{string.Join('\t', events)}\tafter:{after}");
}

void FillDictionaries(object instance)
{
    foreach (var entry in new (string Name, float Value)[] {
        ("AccelerateDictionary", 0.125f), ("VelocityDictionary", 7f),
        ("DirectionVelocityDegreeDictionary", -90f), ("DirectionDegreeDictionary", 180f),
        ("TransparentVelocityDictionary", -0.5f), ("ScaleWidthVelocityDictionary", 0.25f),
        ("ScaleLengthVelocityDictionary", -0.25f) })
        ((IDictionary)baseType.GetField(entry.Name)!.GetValue(instance)!).Add(1, entry.Value);
    ((IDictionary)baseType.GetField("DestPointDictionary")!.GetValue(instance)!).Add(1, new PointF(41, -43));
}

static Type MakeMovingProbe(Type baseType)
{
    var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("DegreeMovingProbe"), AssemblyBuilderAccess.Run).DefineDynamicModule("Main");
    var type = module.DefineType("MovingProbe", TypeAttributes.Public, baseType);
    var move = type.DefineMethod("Move", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig, typeof(void), Type.EmptyTypes);
    var il = move.GetILGenerator();
    il.Emit(OpCodes.Ldarg_0);
    il.Emit(OpCodes.Call, typeof(ProbeHooks).GetMethod(nameof(ProbeHooks.Move))!);
    il.Emit(OpCodes.Ret);
    type.DefineMethodOverride(move, baseType.GetMethod("Move", Type.EmptyTypes)!);
    return type.CreateType()!;
}

static string Bits(object? value)
{
    if (value == null) return "null";
    if (value is float f) return BitConverter.SingleToInt32Bits(f).ToString("X8");
    if (value is double d) return BitConverter.DoubleToInt64Bits(d).ToString("X16");
    if (value is string s) return "string:" + s;
    if (value is bool b) return b ? "true" : "false";
    if (value is Delegate callback) return "delegate:" + callback.GetType().FullName + ":" + callback.GetInvocationList().Length;
    if (value is IDictionary dictionary)
        return "{" + string.Join(';', dictionary.Keys.Cast<object>().OrderBy(k => Convert.ToInt32(k, CultureInfo.InvariantCulture)).Select(k => Bits(k) + "=" + Bits(dictionary[k]))) + "}";
    if (value is Array array) return "[" + string.Join(';', array.Cast<object?>().Select(Bits)) + "]";
    Type type = value.GetType();
    if (type.IsPrimitive || type.IsEnum) return Convert.ToString(value, CultureInfo.InvariantCulture)!;
    if (type.IsValueType)
        return type.FullName + "{" + string.Join(';', type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => f.Name + "=" + Bits(f.GetValue(value)))) + "}";
    throw new InvalidOperationException("Uncaptured reference state: " + type.FullName);
}

public static class ProbeHooks
{
    public static Action<object> OnMove = _ => throw new InvalidOperationException("No active case");
    public static void Move(object instance) => OnMove(instance);
}
