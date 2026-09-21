using System;

namespace MControlTray;

internal readonly struct Scenario
{
    public readonly string Name;
    public readonly int Index;
    public readonly string Icon;

    public Scenario(string name, int index, string icon)
    {
        Name = name;
        Index = index;
        Icon = icon;
    }
}

// The scenario catalog plus the order the cycle hotkey walks through.
// Index values are MSI's own and were verified on board 15P2 - see README.
internal static class Scenarios
{
    public const int ExtremeIndex = 1;
    public const int BalancedIndex = 2;
    public const int SilentIndex = 4;

    public static readonly Scenario[] All =
    {
        new Scenario("Extreme Performance", ExtremeIndex,  "e.ico"),
        new Scenario("Balanced",            BalancedIndex, "b.ico"),
        new Scenario("ECO / Silent",        SilentIndex,   "s.ico"),
    };

    public static Scenario? FindByIndex(int index)
    {
        foreach (Scenario s in All)
            if (s.Index == index)
                return s;
        return null;
    }

    // Command-line argument to scenario index; -1 when the argument is unknown.
    public static int MatchIndex(string arg)
    {
        switch (arg.TrimStart('-', '/').ToLowerInvariant())
        {
            case "extreme":
            case "e":
            case "1":
                return ExtremeIndex;
            case "balanced":
            case "b":
            case "2":
                return BalancedIndex;
            case "silent":
            case "eco":
            case "s":
            case "4":
                return SilentIndex;
            default:
                return -1;
        }
    }

    // Next scenario in All, wrapping around. An unknown current state starts at
    // Balanced, so the hotkey never drops an unaware machine into Extreme.
    public static int NextIndex(int currentIndex)
    {
        for (int i = 0; i < All.Length; i++)
            if (All[i].Index == currentIndex)
                return All[(i + 1) % All.Length].Index;
        return BalancedIndex;
    }
}
