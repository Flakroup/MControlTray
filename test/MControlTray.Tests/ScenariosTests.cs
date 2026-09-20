using MControlTray;
using Shouldly;
using Xunit;

namespace MControlTray.Tests;

public sealed class ScenariosTests
{
    [Theory]
    [InlineData("--extreme", Scenarios.ExtremeIndex)]
    [InlineData("-e", Scenarios.ExtremeIndex)]
    [InlineData("/1", Scenarios.ExtremeIndex)]
    [InlineData("--EXTREME", Scenarios.ExtremeIndex)]
    [InlineData("--balanced", Scenarios.BalancedIndex)]
    [InlineData("b", Scenarios.BalancedIndex)]
    [InlineData("2", Scenarios.BalancedIndex)]
    [InlineData("--silent", Scenarios.SilentIndex)]
    [InlineData("--eco", Scenarios.SilentIndex)]
    [InlineData("/s", Scenarios.SilentIndex)]
    [InlineData("4", Scenarios.SilentIndex)]
    public void MatchIndex_maps_known_arguments(string argument, int expected)
    {
        Scenarios.MatchIndex(argument).ShouldBe(expected);
    }

    [Theory]
    [InlineData("--turbo")]
    [InlineData("")]
    [InlineData("3")]
    [InlineData("--")]
    public void MatchIndex_rejects_unknown_arguments(string argument)
    {
        Scenarios.MatchIndex(argument).ShouldBe(-1);
    }

    [Theory]
    [InlineData(Scenarios.ExtremeIndex, Scenarios.BalancedIndex)]
    [InlineData(Scenarios.BalancedIndex, Scenarios.SilentIndex)]
    [InlineData(Scenarios.SilentIndex, Scenarios.ExtremeIndex)]
    public void NextIndex_cycles_through_the_catalog(int current, int expected)
    {
        Scenarios.NextIndex(current).ShouldBe(expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(99)]
    public void NextIndex_starts_at_Balanced_when_the_current_state_is_unknown(int current)
    {
        Scenarios.NextIndex(current).ShouldBe(Scenarios.BalancedIndex);
    }

    [Fact]
    public void NextIndex_visits_every_scenario_before_repeating()
    {
        int first = Scenarios.ExtremeIndex;
        int second = Scenarios.NextIndex(first);
        int third = Scenarios.NextIndex(second);

        Scenarios.NextIndex(third).ShouldBe(first);
        new[] { first, second, third }.ShouldBeUnique();
    }

    [Fact]
    public void FindByIndex_returns_the_matching_scenario()
    {
        Scenarios.FindByIndex(Scenarios.SilentIndex)!.Value.Name.ShouldBe("ECO / Silent");
        Scenarios.FindByIndex(42).ShouldBeNull();
    }
}
