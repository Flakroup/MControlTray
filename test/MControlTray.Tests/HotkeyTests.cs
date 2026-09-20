using MControlTray;
using Shouldly;
using Xunit;

namespace MControlTray.Tests;

public sealed class HotkeyTests
{
    [Fact]
    public void TryParse_reads_modifiers_and_key()
    {
        Hotkey.TryParse("Ctrl+Alt+Shift+P", out Hotkey hotkey).ShouldBeTrue();

        hotkey.Modifiers.ShouldBe(Hotkey.ModControl | Hotkey.ModAlt | Hotkey.ModShift);
        hotkey.VirtualKey.ShouldBe((uint)'P');
        hotkey.IsEnabled.ShouldBeTrue();
    }

    [Theory]
    [InlineData("ctrl+alt+shift+p")]
    [InlineData("  Ctrl + Alt + Shift + p  ")]
    [InlineData("CONTROL+ALT+SHIFT+P")]
    public void TryParse_ignores_case_and_surrounding_whitespace(string text)
    {
        Hotkey.TryParse(text, out Hotkey hotkey).ShouldBeTrue();

        hotkey.ShouldBe(new Hotkey(Hotkey.ModControl | Hotkey.ModAlt | Hotkey.ModShift, 'P'));
    }

    [Theory]
    [InlineData("Ctrl+F1", 0x70u)]
    [InlineData("Ctrl+f12", 0x7Bu)]
    [InlineData("Win+F24", 0x87u)]
    public void TryParse_reads_function_keys(string text, uint expectedKey)
    {
        Hotkey.TryParse(text, out Hotkey hotkey).ShouldBeTrue();

        hotkey.VirtualKey.ShouldBe(expectedKey);
    }

    [Fact]
    public void TryParse_reads_digits_and_the_Windows_modifier()
    {
        Hotkey.TryParse("Win+7", out Hotkey hotkey).ShouldBeTrue();

        hotkey.Modifiers.ShouldBe(Hotkey.ModWin);
        hotkey.VirtualKey.ShouldBe((uint)'7');
    }

    [Theory]
    [InlineData("none")]
    [InlineData("NONE")]
    [InlineData("off")]
    public void TryParse_accepts_an_explicit_opt_out(string text)
    {
        Hotkey.TryParse(text, out Hotkey hotkey).ShouldBeTrue();

        hotkey.IsEnabled.ShouldBeFalse();
        hotkey.Format().ShouldBe("none");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("P")]               // a bare key would swallow normal typing
    [InlineData("F5")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl++P")]
    [InlineData("Ctrl+A+B")]        // two non-modifier keys
    [InlineData("Ctrl+Alt")]        // no key at all
    [InlineData("Ctrl+F25")]        // beyond F24
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+Escape")]     // named keys are not supported
    [InlineData("Ctrl+ą")]
    public void TryParse_rejects_unusable_input(string? text)
    {
        Hotkey.TryParse(text, out Hotkey hotkey).ShouldBeFalse();

        hotkey.ShouldBe(Hotkey.None);
    }

    [Theory]
    [InlineData("Ctrl+Alt+Shift+P")]
    [InlineData("Win+F12")]
    [InlineData("Ctrl+9")]
    public void Format_round_trips_through_TryParse(string text)
    {
        Hotkey.TryParse(text, out Hotkey hotkey).ShouldBeTrue();

        hotkey.Format().ShouldBe(text);
    }

    [Fact]
    public void Format_orders_modifiers_predictably()
    {
        Hotkey hotkey = new Hotkey(Hotkey.ModWin | Hotkey.ModShift | Hotkey.ModAlt | Hotkey.ModControl, 'K');

        hotkey.Format().ShouldBe("Ctrl+Alt+Shift+Win+K");
    }
}
