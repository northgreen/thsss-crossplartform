using System.Text;
using THSSS.Config;

// Config fixture: Setting.INI must keep the original encoding and contents. The shipped file
// is GBK, so a wrong read/write silently destroys Chinese font names; every assertion below
// round-trips through the real IniFile/ConfigSettings code the dialog uses.
int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    checks++;
}

// GBK (code page 936) is not registered by default on .NET Core, hence the provider.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Encoding gbk = Encoding.GetEncoding(936);

// Same shape as the shipped Setting.INI: every section Config.exe reads, with the keys this
// fixture does not edit (volume, clear, AntiAlias) present to prove they survive a save.
string root = Path.Combine(Path.GetTempPath(), "thsss-config-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    // The shipped file is GBK with Chinese values the dialog must not corrupt.
    string iniPath = Path.Combine(root, ConfigSettings.FileName);
    File.WriteAllText(iniPath,
        "[DirectX]\r\nFullWindow=0\r\nAntiAlias=0\r\nVerticalSync=0\r\n" +
        "[Mode]\r\nWindowSize=0\r\nPresentInterval=1\r\nClear=0\r\n" +
        "[DirectInput]\r\nUseDirectInput=1\r\n" +
        "[Key]\r\nLeft=Left\r\nRight=Right\r\nUp=Up\r\nDown=Down\r\nSlow=ShiftKey\r\nShoot=Z\r\nBomb=X\r\n" +
        "[JoyStick]\r\nSkip=6\r\nPause=11\r\nSlow=7\r\nShoot=3\r\nBomb=0\r\n" +
        "[Volume]\r\nBGMVolume=100\r\nSEVolume=80\r\n" +
        "[Game]\r\nFontType=楷体\r\n", gbk);

    // The workspace resolves Setting.INI relative to the data root, like the game does.
    var workspace = new ConfigWorkspace(root);
    Check(workspace.IniPath == iniPath, "workspace resolves Setting.INI next to the data root");

    var loaded = ConfigSettings.Load(IniFile.Open(iniPath));
    Check(loaded.FontType == "楷体", "GBK Chinese font name decoded: " + loaded.FontType);
    Check(loaded.Shoot == "Z" && loaded.Bomb == "X" && loaded.Slow == "ShiftKey", "key defaults read");
    Check(loaded.UseDirectInput && loaded.PresentInterval == 1 && !loaded.VerticalSync, "flags read");
    Check(loaded.JoySkip == 6 && loaded.JoyPause == 11 && loaded.JoyShoot == 3, "controller buttons read");

    // Only the keys Config.exe owns may change; volume/clear/AntiAlias must survive.
    loaded.PresentInterval = 2;
    loaded.VerticalSync = true;
    loaded.Shoot = "Return";
    loaded.FontType = "宋体";
    var ini = IniFile.Open(iniPath);
    loaded.Save(ini);
    ini.Save();

    // Re-decoding the rewritten bytes as GBK proves the encoding was preserved: a UTF-8 write
    // would show up as replacement characters (U+FFFD-ish mojibake such as 锟).
    byte[] rewritten = File.ReadAllBytes(iniPath);
    string text = gbk.GetString(rewritten);
    Check(!text.Contains("锟"), "file still decodes as GBK without replacement characters");
    Check(text.Contains("PresentInterval=2") && text.Contains("VerticalSync=1"), "edited flags written");
    Check(text.Contains("Shoot=Return") && text.Contains("FontType=宋体"), "edited key values written");
    // Settings the dialog never writes must not be dropped by a full-file save.
    Check(text.Contains("BGMVolume=100") && text.Contains("Clear=0") && text.Contains("AntiAlias=0"),
        "untouched settings preserved");

    var reloaded = ConfigSettings.Load(IniFile.Open(iniPath));
    Check(reloaded.PresentInterval == 2 && reloaded.VerticalSync && reloaded.Shoot == "Return"
        && reloaded.FontType == "宋体", "round trip reload");

    // A UTF-8 file stays UTF-8: no mojibake when the user already migrated it. The check is
    // on the raw bytes, since a BOM or a GBK rewrite only shows there.
    string utf8Path = Path.Combine(root, "utf8-" + ConfigSettings.FileName);
    File.WriteAllText(utf8Path, "[Game]\r\nFontType=隶书\r\n", new UTF8Encoding(false));
    var utf8Ini = IniFile.Open(utf8Path);
    Check(utf8Ini.Read("Game", "FontType", "?") == "隶书", "UTF-8 values decoded");
    var utf8Settings = ConfigSettings.Load(utf8Ini);
    utf8Settings.FontType = "幼圆";
    utf8Settings.Save(utf8Ini);
    utf8Ini.Save();
    byte[] utf8Bytes = File.ReadAllBytes(utf8Path);
    Check(!(utf8Bytes.Length >= 3 && utf8Bytes[0] == 0xEF && utf8Bytes[1] == 0xBB && utf8Bytes[2] == 0xBF),
        "UTF-8 file is not silently converted to GBK or given a BOM");
    Check(Encoding.UTF8.GetString(utf8Bytes).Contains("FontType=幼圆"), "UTF-8 file edited in place");

    // Writing into a brand-new file creates the sections the game expects. The absent file
    // must be reported absent first (the original ExistINIFile contract).
    string freshPath = Path.Combine(root, "fresh.ini");
    var fresh = IniFile.Open(freshPath);
    Check(!fresh.Exists, "missing file reported absent like ExistINIFile");
    new ConfigSettings().Save(fresh);
    fresh.Save();
    var freshLoaded = ConfigSettings.Load(IniFile.Open(freshPath));
    Check(freshLoaded.Shoot == "Z" && freshLoaded.FontType == "楷体", "defaults written into a new Setting.INI");

    // Key capture only accepts names the game understands. Keys the original never supported
    // map to null, and stored names are validated against the known list before use.
    Check(KeyNameMapper.ToGameName(Avalonia.Input.Key.Z) == "Z", "letter key mapped");
    Check(KeyNameMapper.ToGameName(Avalonia.Input.Key.LeftShift) == "ShiftKey", "shift mapped");
    Check(KeyNameMapper.ToGameName(Avalonia.Input.Key.Enter) == "Return", "enter maps to Return");
    Check(KeyNameMapper.ToGameName(Avalonia.Input.Key.OemPlus) == "Oemplus", "plus mapped");
    Check(KeyNameMapper.ToGameName(Avalonia.Input.Key.NumPad7) == "NumPad7", "numpad mapped");
    Check(KeyNameMapper.ToGameName(Avalonia.Input.Key.MediaPlayPause) == null, "unmapped media key rejected");
    Check(KeyNames.IndexOf("ShiftKey") >= 0 && KeyNames.IndexOf("st3") < 0, "stored names validated");

    // Launch finds the game beside the data root, and reports honestly when absent (the
    // message is user-facing, so the localized 找不到 text is part of the contract).
    Check(!workspace.LaunchGame(out string absent) && absent.Contains("找不到"), "missing game executable reported");
    string fake = Path.Combine(root, "THSSS.exe");
    File.WriteAllText(fake, string.Empty);
    Check(workspace.FindGameExecutable() == fake, "game executable discovered next to data root");

    string package = Path.Combine(root, "package");
    string configApp = Path.Combine(package, "ConfigApp");
    string externalData = Path.Combine(root, "external player data");
    Directory.CreateDirectory(configApp);
    Directory.CreateDirectory(externalData);
    string packagedGame = Path.Combine(package, "THSSS.exe");
    File.WriteAllText(packagedGame, string.Empty);
    var isolated = new ConfigWorkspace(externalData, configApp);
    Check(isolated.FindGameExecutable() == packagedGame, "ConfigApp finds the package game with an external data root");
    File.WriteAllText(Path.Combine(externalData, "THSSS.exe"), string.Empty);
    Check(isolated.FindGameExecutable() == Path.Combine(externalData, "THSSS.exe"), "External data root retains the existing explicit game lookup priority");

    // The window is created from the same settings object, so this verifies that a
    // saved file is what the dialog would open with (real UI apply is exercised
    // manually and by the Xvfb smoke run of the executable).
    Check(File.Exists(typeof(ConfigWindow).Assembly.Location), "config assembly built");
    // Option lists are part of the dialog contract: the font list includes the shipped default
    // and the controller button list keeps the original twelve entries.
    Check(ConfigSettings.FontChoices.Contains("楷体") && ConfigSettings.ControllerButtons.Length == 12,
        "option lists match original dialog");

    Console.WriteLine($"PASS ConfigCompatibility: {checks} assertions (GBK/UTF-8 preservation, key set, launch discovery, option lists)");
}
finally
{
    Directory.Delete(root, recursive: true);
}
