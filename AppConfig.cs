using System;
using System.IO;

namespace MControlTray;

// Optional user settings in %AppData%\MControlTray\config.ini, written with
// defaults on first run. A missing, unreadable or malformed file never stops
// the tray from starting - it falls back to the defaults and says so.
internal sealed class AppConfig
{
    public const string CycleKey = "cycle";

    public static readonly Hotkey DefaultCycleHotkey =
        new Hotkey(Hotkey.ModControl | Hotkey.ModAlt | Hotkey.ModShift, 'P');

    public Hotkey CycleHotkey { get; private set; } = DefaultCycleHotkey;

    // User-facing warning about a bad value, null when the file was fine.
    public string? Warning { get; private set; }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MControlTray", "config.ini");

    public static AppConfig Load(string path)
    {
        AppConfig config = new AppConfig();
        try
        {
            if (!File.Exists(path))
            {
                WriteTemplate(path);
                return config;
            }

            foreach (string line in File.ReadAllLines(path))
            {
                if (!TrySplit(line, out string key, out string value))
                    continue;
                if (!string.Equals(key, CycleKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (Hotkey.TryParse(value, out Hotkey parsed))
                    config.CycleHotkey = parsed;
                else
                    config.Warning = "Nieczytelny skrót w config.ini: \"" + value
                        + "\". Używam " + DefaultCycleHotkey.Format() + ".";
            }
        }
        catch (Exception)
        {
            // defaults are good enough; never block startup on the config file
        }
        return config;
    }

    private static bool TrySplit(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;

        string trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == ';')
            return false;

        int separator = trimmed.IndexOf('=');
        if (separator <= 0)
            return false;

        key = trimmed.Substring(0, separator).Trim();
        value = trimmed.Substring(separator + 1).Trim();
        return key.Length > 0;
    }

    private static void WriteTemplate(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllLines(path, new[]
        {
            "# MControlTray configuration.",
            "# Restart MControlTray after editing this file.",
            "#",
            "# cycle - global hotkey that cycles the User Scenario:",
            "#   Extreme Performance -> Balanced -> ECO / Silent -> ...",
            "# Modifiers: Ctrl, Alt, Shift, Win - at least one is required.",
            "# Key: A-Z, 0-9 or F1-F24. Use \"none\" to disable the hotkey.",
            CycleKey + "=" + DefaultCycleHotkey.Format(),
        });
    }
}
