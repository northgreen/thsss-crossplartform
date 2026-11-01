using System.Diagnostics;
using System.Security;
using System.Text.Json;
using SkiaSharp;
using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using System.Text;

internal static class FontconfigChecks
{
    private const string Alias = "THSSS-Private-CJK-Regression-2026";

    internal static int Run()
    {
        if (!OperatingSystem.IsLinux())
        {
            Console.WriteLine("SKIP fontconfig: Linux-only configuration integration");
            return 0;
        }
        string directory = Path.Combine(Path.GetTempPath(), "thsss-fontconfig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var installed = Match("Noto Sans CJK SC:lang=zh-cn:charset=590f", null);
            if (!File.Exists(installed.File)) throw new Exception("fc-match must locate an installed CJK font");
            using (var input = File.OpenRead(installed.File))
            {
                byte[] magic = new byte[4];
                input.ReadExactly(magic);
                if (!magic.AsSpan().SequenceEqual("ttcf"u8) || installed.Index == 0)
                    throw new Exception("TTC regression requires an installed CJK collection with a nonzero SC face index (Noto Sans CJK SC)");
            }
            string fontDirectory = Path.Combine(directory, "fonts");
            Directory.CreateDirectory(fontDirectory);
            string copiedFont = Path.Combine(fontDirectory, Path.GetFileName(installed.File));
            File.Copy(installed.File, copiedFont);
            var latin = Match("DejaVu Serif:charset=0041", null);
            File.Copy(latin.File, Path.Combine(fontDirectory, Path.GetFileName(latin.File)));
            File.WriteAllBytes(Path.Combine(fontDirectory, "broken.ttf"), "not a font\0truncated sfnt"u8.ToArray());
            string config = Path.Combine(directory, "fonts.conf");
            File.WriteAllText(config, $"""
                <?xml version="1.0"?>
                <!DOCTYPE fontconfig SYSTEM "urn:fontconfig:fonts.dtd">
                <fontconfig>
                  <dir>{SecurityElement.Escape(fontDirectory)}</dir>
                  <cachedir>{SecurityElement.Escape(Path.Combine(directory, "cache"))}</cachedir>
                  <match target="pattern"><test name="family"><string>{Alias}</string></test>
                    <edit name="family" mode="assign" binding="strong"><string>{SecurityElement.Escape(installed.Family)}</string></edit>
                  </match>
                </fontconfig>
                """);
            // FcNameParse treats unescaped hyphens as the family/size separator.
            var matched = Match(Alias.Replace("-", "\\-") + ":lang=zh-cn:charset=590f", config);
            if (matched.Index != installed.Index) throw new Exception($"Private alias must retain SC collection index: {matched}");
            if (Path.GetFullPath(matched.File) != Path.GetFullPath(copiedFont))
                throw new Exception("Isolated fontconfig did not use the private directory");
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(typeof(FontconfigChecks).Assembly.Location);
            foreach (string argument in new[] { "fontconfig-child", Alias, matched.File, matched.Index.ToString(), matched.Family, latin.Family })
                start.ArgumentList.Add(argument);
            start.Environment["FONTCONFIG_FILE"] = config;
            start.Environment["FONTCONFIG_PATH"] = directory;
            start.Environment["XDG_CACHE_HOME"] = Path.Combine(directory, "xdg-cache");
            string output = Execute(start);
            Console.Write(output);
            return 8;
        }
        finally { Directory.Delete(directory, true); }
    }

    internal static void RunChild(string[] args)
    {
        int index = int.Parse(args[3]);
        // Use literal TTC tables as the oracle, not a second native loader.
        using var fileFace = SKTypeface.FromFile(args[2], index);
        AssertTtcTables(fileFace, args[2], index);
        using var alias = SKFontManager.Default.MatchFamily(args[1], SKFontStyle.Normal)
            ?? throw new Exception("Native fontconfig private alias not resolved");
        using var fallback = SKFontManager.Default.MatchCharacter("missing-thsss-family", SKFontStyle.Normal, ["zh-CN"], 0x590f)
            ?? throw new Exception("Native fontconfig CJK codepoint/language fallback missing");
        AssertTtcTables(alias, args[2], index);
        var expectedFallback = Match("missing\\-thsss\\-family:lang=zh-cn:charset=590f", Environment.GetEnvironmentVariable("FONTCONFIG_FILE"));
        AssertTtcTables(fallback, expectedFallback.File, expectedFallback.Index);
        using var fallbackFile = SKTypeface.FromFile(expectedFallback.File, expectedFallback.Index);
        using var aliasFont = new SKFont(alias, 24);
        using var fallbackFont = new SKFont(fallback, 24);
        using var fallbackFileFont = new SKFont(fallbackFile, 24);
        foreach (int codepoint in new[] { 0x590f, 0x591c, 0x5bf9, 0x767d })
        {
            if (aliasFont.GetGlyph(codepoint) == 0 || fallbackFont.GetGlyph(codepoint) == 0 ||
                fallbackFont.GetGlyph(codepoint) != fallbackFileFont.GetGlyph(codepoint))
                throw new Exception($"CJK actual glyph fallback failed U+{codepoint:X}");
        }
        // Different CJK faces can advertise the same language coverage. Compare each
        // query with fontconfig's own result, not an unrelated strong-family alias.
        AssertSameFace(fallback, fallbackFile, "language/codepoint fallback");
        var helper = typeof(System.Drawing.Bitmap).Assembly.GetType("System.Drawing.SkiaTextFonts", true)!;
        using var requested = new System.Drawing.Font(args[1], 24, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
        var productionAlias = (SKTypeface)helper.GetMethod("Primary", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [requested])!;
        AssertTtcTables(productionAlias, args[2], index);
        using var latin = new System.Drawing.Font(args[5], 24, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
        var productionPrimary = (SKTypeface)helper.GetMethod("Primary", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [latin])!;
        using (var coverage = new SKFont(productionPrimary))
            if (coverage.ContainsGlyph(0x590f)) throw new Exception("Fallback fixture must really lack the CJK glyph");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
        var productionFallback = (SKTypeface)helper.GetMethod("ForRune", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [latin, new Rune(0x590f)])!;
        var expectedProduction = Match(args[5].Replace("-", "\\-") + ":lang=zh-cn:charset=590f", Environment.GetEnvironmentVariable("FONTCONFIG_FILE"));
        AssertTtcTables(productionFallback, expectedProduction.File, expectedProduction.Index);
        Console.WriteLine($"PASS fontconfig: private alias TTC index={args[3]} ({args[4]}), custom directory and zh-CN/codepoint fallback index={expectedFallback.Index} ({expectedFallback.Family}) match fc-match glyph/table identity");
        Console.WriteLine("PASS production font resolver: private alias and real missing-CJK fallback match native file/face tables");
    }

    private static void AssertTtcTables(SKTypeface face, string path, int index)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12 + index * 4)));
        int count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 4));
        for (int i = 0; i < count; i++)
        {
            var record = bytes.AsSpan(offset + 12 + i * 16, 16);
            uint tag = BinaryPrimitives.ReadUInt32BigEndian(record);
            int start = checked((int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]));
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]));
            if (!face.GetTableData(tag).AsSpan().SequenceEqual(bytes.AsSpan(start, length)))
                throw new Exception($"TTC table {tag:X8} differs from literal collection index {index}: {face.FamilyName}");
        }
    }

    private static void AssertSameFace(SKTypeface actual, SKTypeface expected, string description)
    {
        // Family names alone do not distinguish collection faces or altered glyph data.
        foreach (uint tag in new uint[] { 0x636d6170, 0x686d7478, 0x4f532f32 })
            if (!actual.GetTableData(tag).AsSpan().SequenceEqual(expected.GetTableData(tag)))
                throw new Exception($"Fontconfig selected different font table {tag:X8}: {description}; actual={actual.FamilyName}, expected={expected.FamilyName}");
    }

    private static (string File, int Index, string Family) Match(string pattern, string config)
    {
        var start = new ProcessStartInfo("fc-match") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--format=%{file}\n%{index}\n%{family[0]}\n");
        start.ArgumentList.Add(pattern);
        if (config != null)
        {
            start.Environment["FONTCONFIG_FILE"] = config;
            start.Environment["FONTCONFIG_PATH"] = Path.GetDirectoryName(config);
        }
        string[] lines = Execute(start).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length != 3 || !int.TryParse(lines[1], out int index))
            throw new Exception("Unexpected fc-match output: " + JsonSerializer.Serialize(lines));
        return (lines[0], index, lines[2]);
    }

    private static string Execute(ProcessStartInfo start)
    {
        using var process = Process.Start(start) ?? throw new Exception("Could not launch " + start.FileName);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(true);
            throw new TimeoutException(start.FileName + " timed out");
        }
        Task.WaitAll(stdout, stderr);
        if (process.ExitCode != 0) throw new Exception($"{start.FileName} exited {process.ExitCode}: {stderr.Result}\n{stdout.Result}");
        return stdout.Result;
    }
}
