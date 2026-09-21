using System;

namespace MControlTray;

// Headless mode: MControlTray.exe --balanced. The exit codes are a contract
// documented in the README, so shortcuts and scripts can react to them.
internal static class CommandLine
{
    public const int ExitOk = 0;
    public const int ExitSendFailed = 1;
    public const int ExitBadArgument = 2;

    public static int Run(string[] args, Action<int> apply)
    {
        if (args.Length == 0)
            return ExitBadArgument;

        int index = Scenarios.MatchIndex(args[0]);
        if (index < 0)
            return ExitBadArgument;

        try
        {
            apply(index);
            return ExitOk;
        }
        catch (Exception)
        {
            return ExitSendFailed;
        }
    }
}
