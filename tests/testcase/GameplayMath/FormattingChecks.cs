using System.Globalization;
using System.IO.Compression;
using System.Text;
using SlimDX.Compat;

if (args.Length != 1) throw new ArgumentException("Expected formatting fixture directory");
string root = Path.GetFullPath(args[0]);
byte[] inputs = File.ReadAllBytes(Path.Combine(root, "formatting-inputs.bin"));
if (inputs.Length % 4 != 0) throw new InvalidDataException("Truncated formatting input");
int count = 0;
var saved = CultureInfo.CurrentCulture;
try
{
    foreach (int precision in new[] { 24, 53 })
    {
        string file = Path.Combine(root, $"formatting-clr2-{precision}.tsv");
        using Stream captured = File.Exists(file) ? File.OpenRead(file) : new GZipStream(File.OpenRead(file + ".gz"), CompressionMode.Decompress);
        using var reader = new StreamReader(captured);
        foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("zh-CN") })
        {
            CultureInfo.CurrentCulture = culture;
            for (int offset = 0; offset < inputs.Length; offset += 4)
            {
                string[] row = (reader.ReadLine() ?? throw new InvalidDataException("Incomplete formatting capture")).Split('\t');
                string inputBits = BitConverter.ToInt32(inputs, offset).ToString("X8", CultureInfo.InvariantCulture);
                string name = culture.Name.Length == 0 ? "Invariant" : culture.Name;
                if (row.Length != 4 || row[0] != (offset / 4).ToString(CultureInfo.InvariantCulture) || row[1] != name || row[2] != inputBits)
                    throw new InvalidDataException("Formatting input identity/order differs");
                string expected = Encoding.UTF8.GetString(Convert.FromBase64String(row[3]));
                string actual = LegacyFloat.FormatScriptRegion(BitConverter.ToSingle(inputs, offset));
                if (actual != expected) throw new InvalidOperationException($"Script region PC{precision} {name} bits={inputBits}: expected={expected} actual={actual}");
                count++;
            }
        }
        if (reader.ReadLine() != null) throw new InvalidDataException("Unexpected trailing formatting output");
    }
}
finally { CultureInfo.CurrentCulture = saved; }
Console.WriteLine($"LEGACY_SCRIPT_FORMATTING_PASS rows={count}; finite default G7; decimal ties; signed zero; two cultures; two states");
