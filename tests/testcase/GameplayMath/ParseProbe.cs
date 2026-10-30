using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static class ParseProbe
{
    public static int Main(string[] args)
    {
        if (args.Length < 2 || args.Length > 3)
        {
            Console.Error.WriteLine("Usage: ParseProbe inputs.b64 output.tsv [state-label]");
            return 2;
        }
        Run(args[0], args[1], args.Length == 3 ? args[2] : "unspecified");
        return 0;
    }

    // An external CLR2/D3D host can call Run before and after device creation.
    // This probe never sets the FPU control word or creates a graphics device.
    public static void Run(string inputPath, string outputPath, string label)
    {
        if (Path.GetFullPath(inputPath) == Path.GetFullPath(outputPath))
            throw new ArgumentException("Input and output paths must differ.");
        if (label.IndexOfAny(new char[] { '\t', '\r', '\n' }) >= 0)
            throw new ArgumentException("State label cannot contain TSV separators.");
        byte[] fileBytes = File.ReadAllBytes(inputPath);
        string identity;
        using (SHA256 hash = SHA256.Create())
            identity = BitConverter.ToString(hash.ComputeHash(fileBytes)).Replace("-", "").ToLowerInvariant();
        string[] lines = File.ReadAllLines(inputPath, Encoding.UTF8);
        CultureInfo[] cultures = new CultureInfo[] {
            CultureInfo.InvariantCulture, new CultureInfo("zh-CN") };
        using (StreamWriter writer = new StreamWriter(outputPath, false, new UTF8Encoding(false)))
        {
            writer.NewLine = "\n";
            writer.WriteLine("# input_sha256=" + identity);
            writer.WriteLine("# runtime=" + Environment.Version.ToString() + " state=" + label);
            writer.WriteLine("id\tculture\ttype\tstatus\tbits\tinput_base64");
            for (int i = 0; i < lines.Length; i++)
            {
                string token = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(lines[i]));
                foreach (CultureInfo culture in cultures)
                {
                    WriteResult(writer, i, culture, true, token, lines[i]);
                    WriteResult(writer, i, culture, false, token, lines[i]);
                }
            }
        }
    }

    private static string ParseBits(string token, CultureInfo culture, bool single)
    {
        if (single)
        {
            float value = Single.Parse(token, culture);
            uint bits = unchecked((uint)BitConverter.ToInt32(BitConverter.GetBytes(value), 0));
            return bits.ToString("X8", CultureInfo.InvariantCulture);
        }
        double number = Double.Parse(token, culture);
        ulong doubleBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(number));
        return doubleBits.ToString("X16", CultureInfo.InvariantCulture);
    }

    private static void WriteResult(StreamWriter writer, int id, CultureInfo culture,
                                    bool single, string token, string encoded)
    {
        string status = "ok";
        string bits = "-";
        try { bits = ParseBits(token, culture, single); }
        catch (FormatException) { status = "System.FormatException"; }
        catch (OverflowException) { status = "System.OverflowException"; }
        writer.WriteLine(id.ToString(CultureInfo.InvariantCulture) + "\t" +
            (culture.Name.Length == 0 ? "Invariant" : culture.Name) + "\t" +
            (single ? "Single" : "Double") + "\t" + status + "\t" + bits + "\t" + encoded);
    }
}
