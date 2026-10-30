using System.IO.Compression;

internal static class ParsingChecks
{
    public static void Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Expected parsing fixture directory");
        string root = Path.GetFullPath(args[0]);
        string modern = Path.GetTempFileName();
        bool success = false;
        try
        {
            ParseProbe.Run(Path.Combine(root, "parse-inputs.b64"), modern, "modern-validation");
            string[] actual = File.ReadAllLines(modern);
            if (actual.Length != 5031) throw new InvalidDataException("Incomplete actual-asset parsing output");
            foreach (int precision in new[] { 24, 53 })
            {
                string file = Path.Combine(root, $"parsing-clr2-{precision}.tsv");
                using Stream capture = File.Exists(file) ? File.OpenRead(file) : new GZipStream(File.OpenRead(file + ".gz"), CompressionMode.Decompress);
                using var expected = new StreamReader(capture);
                for (int row = 0; row < actual.Length; row++)
                {
                    string historical = expected.ReadLine();
                    // The runtime/state header is diagnostic; input identity and all actual records must match.
                    if (row != 1 && actual[row] != historical)
                        throw new InvalidOperationException($"Actual asset parsing PC{precision} row {row}: old={historical}; modern={actual[row]}");
                }
                if (expected.ReadLine() != null) throw new InvalidDataException("Trailing historical parsing output");
            }
            success = true;
            Console.WriteLine("LEGACY_ASSET_PARSING_PASS rows=10056; 1257 literals; 937 assets; Single/Double; two cultures; two states");
        }
        finally
        {
            if (success) File.Delete(modern);
            else Console.Error.WriteLine("Parsing failure output retained at " + modern);
        }
    }
}
