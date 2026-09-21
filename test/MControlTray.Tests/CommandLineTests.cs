using System;
using System.Collections.Generic;
using MControlTray;
using Shouldly;
using Xunit;

namespace MControlTray.Tests;

public sealed class CommandLineTests
{
    [Fact]
    public void Run_applies_the_matching_scenario_and_reports_success()
    {
        List<int> applied = new List<int>();

        int exitCode = CommandLine.Run(new[] { "--silent" }, applied.Add);

        exitCode.ShouldBe(CommandLine.ExitOk);
        applied.ShouldBe(new[] { Scenarios.SilentIndex });
    }

    [Fact]
    public void Run_ignores_extra_arguments()
    {
        List<int> applied = new List<int>();

        CommandLine.Run(new[] { "--balanced", "--extreme" }, applied.Add).ShouldBe(CommandLine.ExitOk);

        applied.ShouldBe(new[] { Scenarios.BalancedIndex });
    }

    [Theory]
    [InlineData("--turbo")]
    [InlineData("")]
    public void Run_reports_an_unknown_argument_without_switching(string argument)
    {
        bool applied = false;

        int exitCode = CommandLine.Run(new[] { argument }, _ => applied = true);

        exitCode.ShouldBe(CommandLine.ExitBadArgument);
        applied.ShouldBeFalse();
    }

    [Fact]
    public void Run_reports_an_empty_command_line_as_a_bad_argument()
    {
        CommandLine.Run(Array.Empty<string>(), _ => { }).ShouldBe(CommandLine.ExitBadArgument);
    }

    [Fact]
    public void Run_reports_a_failed_switch()
    {
        int exitCode = CommandLine.Run(new[] { "--extreme" }, _ => throw new InvalidOperationException("service down"));

        exitCode.ShouldBe(CommandLine.ExitSendFailed);
    }
}
