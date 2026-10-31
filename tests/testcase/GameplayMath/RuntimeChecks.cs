#nullable enable
using System.IO.Compression;
using System.Runtime.InteropServices;
using SlimDX.Compat;

if (args.Length != 1) throw new ArgumentException("Expected GameplayMath fixture directory");
string root = Path.GetFullPath(args[0]);
if (Marshal.SizeOf<LegacyExtended>() != 16) throw new InvalidOperationException("Extended ABI must be 16 bytes");
string requestedBackend = Environment.GetEnvironmentVariable("THSSS_MATH_BACKEND") ?? "auto";
string expectedBackend = requestedBackend is "auto" or "" ?
    (RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "x87" : "software") : requestedBackend;
if (LegacyFloat.Backend != expectedBackend)
    throw new InvalidOperationException($"Gameplay math selected {LegacyFloat.Backend}; expected {expectedBackend}");
Console.WriteLine("LEGACY_BACKEND_SELECTION_PASS backend=" + LegacyFloat.Backend);
var inputs = new List<double>();
using (var input = new BinaryReader(File.OpenRead(Path.Combine(root, "conversion-inputs.bin"))))
{
    if (input.BaseStream.Length % 8 != 0) throw new InvalidDataException("Truncated conversion input");
    while (input.BaseStream.Position != input.BaseStream.Length) inputs.Add(input.ReadDouble());
}
int checkedRows = 0;
foreach (int precision in new[] { 24, 53 })
{
    LegacyFloat.Precision = precision;
    using Stream captured = OpenCapture(root, $"conversion-windows-clr2-{precision}.csv");
    using var expected = new StreamReader(captured);
    for (int i = 0; i < inputs.Count; i++)
    for (int mode = 0; mode < 14; mode++)
    {
        string[] row = (expected.ReadLine() ?? throw new InvalidDataException("Truncated old conversion capture")).Split(',');
        if (row.Length != 4 || row[0] != mode.ToString() || row[1] != i.ToString()) throw new InvalidDataException("Conversion keys differ");
        var extended = LegacyFloat.FromDouble(inputs[i]);
        string inputBits = BitConverter.DoubleToInt64Bits(LegacyFloat.ToDouble(extended)).ToString("X16");
        if (row[2] != inputBits) throw new InvalidDataException($"Original input storage differs index={i}: {row[2]} != {inputBits}");
        string actual;
        try { actual = Convert(mode, extended); }
        catch (OverflowException) { actual = "OverflowException"; }
        if (actual != row[3]) throw new InvalidOperationException($"Conversion PC{precision} mode={mode} input={inputBits}: expected={row[3]} actual={actual}");
        checkedRows++;
    }
    if (expected.ReadLine() != null) throw new InvalidDataException("Unexpected trailing conversion output");
}

string mathRoot = Path.GetFullPath(Path.Combine(root, "..", "MathBaseline"));
var mathInputs = new List<(double a, double b, float x, float y)>();
using (var input = new BinaryReader(File.OpenRead(Path.Combine(mathRoot, "inputs.bin"))))
{
    if (input.BaseStream.Length != 98304) throw new InvalidDataException("Invalid scalar math input size");
    for (int i = 0; i < 4096; i++)
    {
        mathInputs.Add((input.ReadDouble(), input.ReadDouble(), input.ReadSingle(), input.ReadSingle()));
    }
}
int scalarRows = 0;
int vectorRows = 0;
foreach (int precision in new[] { 24, 53 })
{
    LegacyFloat.Precision = precision;
    string name = precision == 24 ? "windows-clr2-d3d-x86.csv" : "windows-clr2-x86.csv";
    using var captured = OpenCapture(mathRoot, name);
    using var reader = new StreamReader(captured);
    string? line;
    while ((line = reader.ReadLine()) != null)
    {
        bool vector = line.StartsWith("vector2.", StringComparison.Ordinal);
        if (!(vector || line.StartsWith("sin,", StringComparison.Ordinal) || line.StartsWith("cos,", StringComparison.Ordinal) ||
              line.StartsWith("atan2,", StringComparison.Ordinal) || line.StartsWith("sqrt,", StringComparison.Ordinal))) continue;
        string[] row = line.Split(',');
        var values = mathInputs[int.Parse(row[1], System.Globalization.CultureInfo.InvariantCulture)];
        if (vector)
        {
            var value = new SlimDX.Vector2(values.x, values.y);
            float vectorResult;
            if (row[0] == "vector2.length") vectorResult = LegacyFloat.ToSingle(LegacyFloat.Vector2Length(ref value));
            else
            {
                LegacyFloat.Vector2Normalize(ref value);
                vectorResult = row[0] switch { "vector2.normalize.x" => value.X, "vector2.normalize.y" => value.Y,
                    _ => throw new InvalidDataException("Unsupported gameplay vector key") };
            }
            string actual = BitConverter.SingleToInt32Bits(vectorResult).ToString("X8");
            if (actual != row[3]) throw new InvalidOperationException($"Gameplay vector PC{precision} {row[0]}[{row[1]}] expected={row[3]} actual={actual}");
            vectorRows++;
            continue;
        }
        var result = row[0] switch
        {
            "sin" => LegacyFloat.Sin(LegacyFloat.FromDouble(values.a)),
            "cos" => LegacyFloat.Cos(LegacyFloat.FromDouble(values.a)),
            "atan2" => LegacyFloat.Atan2(LegacyFloat.FromDouble(values.a), LegacyFloat.FromDouble(values.b)),
            "sqrt" => LegacyFloat.Sqrt(LegacyFloat.FromDouble(values.b)),
            _ => throw new InvalidDataException("Unsupported scalar key")
        };
        string bits = BitConverter.DoubleToInt64Bits(LegacyFloat.ToDouble(result)).ToString("X16");
        if (bits != row[3]) throw new InvalidOperationException($"Scalar math PC{precision} {row[0]}[{row[1]}] expected={row[3]} actual={bits}");
        scalarRows++;
    }
}
if (scalarRows != 32768) throw new InvalidDataException("Incomplete old scalar math capture");
if (vectorRows != 24576) throw new InvalidDataException("Incomplete old gameplay vector capture");
foreach (int precision in new[] { 24, 53 })
{
    LegacyFloat.Precision = precision;
    foreach (double y in new[] { double.PositiveInfinity, double.NegativeInfinity })
    foreach (double x in new[] { double.PositiveInfinity, double.NegativeInfinity })
    {
        var result = LegacyFloat.Atan2(LegacyFloat.FromDouble(y), LegacyFloat.FromDouble(x));
        // Actual CLR2 x86 returns its negative quiet NaN for all four infinity pairs.
        if (unchecked((ulong)BitConverter.DoubleToInt64Bits(LegacyFloat.ToDouble(result))) != 0xfff8000000000000UL)
            throw new InvalidOperationException("Legacy Atan2 infinity wrapper differs from actual CLR2");
    }
}

LegacyFloat.Precision = 24;
int childInitial = 0, childChanged = 0;
var thread = new Thread(() => { childInitial = LegacyFloat.Precision; LegacyFloat.Precision = 53; childChanged = LegacyFloat.Precision; });
thread.Start(); thread.Join();
if (childInitial != 53 || childChanged != 53 || LegacyFloat.Precision != 24) throw new InvalidOperationException("Precision leaked across threads");
try { LegacyFloat.Precision = 64; throw new InvalidOperationException("Invalid precision was accepted"); }
catch (ArgumentOutOfRangeException) { }
Console.WriteLine($"LEGACY_RUNTIME_CONVERSIONS_PASS rows={checkedRows}; ABI16; thread isolation; invalid precision rejection");
Console.WriteLine($"LEGACY_RUNTIME_SCALARS_PASS rows={scalarRows}");
Console.WriteLine($"LEGACY_RUNTIME_VECTORS_PASS rows={vectorRows}");

static Stream OpenCapture(string directory, string name)
{
    // Cross-runtime runners may inflate the verified fixture on their host before execution.
    string plain = Path.Combine(directory, name);
    return File.Exists(plain) ? File.OpenRead(plain) : new GZipStream(File.OpenRead(plain + ".gz"), CompressionMode.Decompress);
}

static string Convert(int mode, LegacyExtended value) => mode switch
{
    0 => LegacyFloat.ToInt32(value).ToString("X8"),
    1 => LegacyFloat.ToUInt32(value).ToString("X8"),
    2 => LegacyFloat.ToInt64(value).ToString("X16"),
    3 => LegacyFloat.ToUInt64(value).ToString("X16"),
    4 => LegacyFloat.ToByte(value).ToString("X2"),
    5 => LegacyFloat.ToSByte(value).ToString("X2"),
    6 => LegacyFloat.ToInt16(value).ToString("X4"),
    7 => LegacyFloat.ToUInt16(value).ToString("X4"),
    8 => LegacyFloat.ToInt32Checked(value).ToString("X8"),
    9 => LegacyFloat.ToUInt32Checked(value).ToString("X8"),
    10 => LegacyFloat.ToInt64Checked(value).ToString("X16"),
    11 => LegacyFloat.ToUInt64Checked(value).ToString("X16"),
    12 => LegacyFloat.ToByteChecked(value).ToString("X2"),
    13 => LegacyFloat.ToSByteChecked(value).ToString("X2"),
    _ => throw new ArgumentOutOfRangeException(nameof(mode))
};
