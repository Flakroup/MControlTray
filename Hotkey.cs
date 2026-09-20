using System;
using System.Globalization;
using System.Text;

namespace MControlTray;

// A global hotkey as RegisterHotKey wants it: a modifier mask plus a virtual-key code.
// An entry with VirtualKey == 0 means "no hotkey".
internal readonly record struct Hotkey(uint Modifiers, uint VirtualKey)
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    private const uint VkF1 = 0x70;
    private const uint VkF12 = 0x7B;
    private const int FunctionKeyCount = 24;

    public static Hotkey None => default;

    public bool IsEnabled => VirtualKey != 0;

    // Accepts "Ctrl+Alt+Shift+P", "ctrl+win+f5", "none". A letter or digit needs two
    // modifiers, so a config file cannot quietly steal Ctrl+C from the whole session;
    // F12 is refused because Windows reserves it for the debugger.
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = None;
        if (text is null)
            return false;

        string trimmed = text.Trim();
        if (trimmed.Length == 0)
            return false;
        if (string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "off", StringComparison.OrdinalIgnoreCase))
            return true;

        uint modifiers = 0;
        uint virtualKey = 0;
        foreach (string part in trimmed.Split('+'))
        {
            string token = part.Trim();
            if (token.Length == 0)
                return false;

            uint modifier = MatchModifier(token);
            if (modifier != 0)
            {
                modifiers |= modifier;
                continue;
            }

            if (virtualKey != 0)
                return false; // two non-modifier keys

            virtualKey = MatchKey(token);
            if (virtualKey == 0)
                return false;
        }

        if (virtualKey == 0 || modifiers == 0 || virtualKey == VkF12)
            return false;
        if (!IsFunctionKey(virtualKey) && CountModifiers(modifiers) < 2)
            return false;

        hotkey = new Hotkey(modifiers, virtualKey);
        return true;
    }

    public string Format()
    {
        if (!IsEnabled)
            return "none";

        StringBuilder sb = new StringBuilder();
        if ((Modifiers & ModControl) != 0)
            sb.Append("Ctrl+");
        if ((Modifiers & ModAlt) != 0)
            sb.Append("Alt+");
        if ((Modifiers & ModShift) != 0)
            sb.Append("Shift+");
        if ((Modifiers & ModWin) != 0)
            sb.Append("Win+");
        sb.Append(KeyName(VirtualKey));
        return sb.ToString();
    }

    private static bool IsFunctionKey(uint virtualKey)
        => virtualKey >= VkF1 && virtualKey < VkF1 + FunctionKeyCount;

    private static int CountModifiers(uint modifiers)
        => System.Numerics.BitOperations.PopCount(modifiers);

    private static uint MatchModifier(string token)
    {
        switch (token.ToLowerInvariant())
        {
            case "ctrl":
            case "control":
                return ModControl;
            case "alt":
                return ModAlt;
            case "shift":
                return ModShift;
            case "win":
            case "windows":
                return ModWin;
            default:
                return 0;
        }
    }

    private static uint MatchKey(string token)
    {
        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
                return c;
            return 0;
        }

        if (token.Length is 2 or 3 && (token[0] == 'F' || token[0] == 'f') && token[1] != '0'
            && int.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            && number is >= 1 and <= FunctionKeyCount)
            return VkF1 + (uint)number - 1;

        return 0;
    }

    private static string KeyName(uint virtualKey)
    {
        if (IsFunctionKey(virtualKey))
            return "F" + (virtualKey - VkF1 + 1);
        return ((char)virtualKey).ToString();
    }
}
