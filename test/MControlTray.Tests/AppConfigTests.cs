using System;
using System.IO;
using MControlTray;
using Shouldly;
using Xunit;

namespace MControlTray.Tests;

public sealed class AppConfigTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;

    public AppConfigTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "MControlTray.Tests", Guid.NewGuid().ToString("N"));
        _path = Path.Combine(_directory, "config.ini");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // a leftover temp directory must not fail the test run
        }
    }

    [Fact]
    public void Load_writes_a_template_when_the_file_is_missing()
    {
        AppConfig config = AppConfig.Load(_path);

        File.Exists(_path).ShouldBeTrue();
        config.CycleHotkey.ShouldBe(AppConfig.DefaultCycleHotkey);
        config.Warning.ShouldBeNull();
    }

    [Fact]
    public void The_written_template_parses_back_to_the_default_hotkey()
    {
        AppConfig.Load(_path);

        AppConfig reloaded = AppConfig.Load(_path);

        reloaded.CycleHotkey.ShouldBe(AppConfig.DefaultCycleHotkey);
        reloaded.Warning.ShouldBeNull();
    }

    [Fact]
    public void Load_reads_a_custom_hotkey()
    {
        Write("cycle=Win+F9");

        AppConfig config = AppConfig.Load(_path);

        config.CycleHotkey.ShouldBe(new Hotkey(Hotkey.ModWin, 0x78));
        config.Warning.ShouldBeNull();
    }

    [Fact]
    public void Load_ignores_comments_blank_lines_and_unknown_keys()
    {
        Write("# a comment", "; another one", "", "   ", "unknown=whatever", "  CYCLE  =  Ctrl+Alt+K  ");

        AppConfig config = AppConfig.Load(_path);

        config.CycleHotkey.ShouldBe(new Hotkey(Hotkey.ModControl | Hotkey.ModAlt, 'K'));
        config.Warning.ShouldBeNull();
    }

    [Fact]
    public void Load_falls_back_to_the_default_and_warns_about_a_broken_value()
    {
        Write("cycle=Ctrl+Nope");

        AppConfig config = AppConfig.Load(_path);

        config.CycleHotkey.ShouldBe(AppConfig.DefaultCycleHotkey);
        config.Warning.ShouldNotBeNull();
        config.Warning.ShouldContain("Ctrl+Nope");
    }

    [Fact]
    public void Load_honours_a_disabled_hotkey()
    {
        Write("cycle=none");

        AppConfig config = AppConfig.Load(_path);

        config.CycleHotkey.IsEnabled.ShouldBeFalse();
        config.Warning.ShouldBeNull();
    }

    [Fact]
    public void Load_survives_a_path_it_can_neither_read_nor_create()
    {
        Write("cycle=Win+F9");
        // _path is a file, so treating it as a directory cannot work.
        string unusable = Path.Combine(_path, "config.ini");

        AppConfig config = AppConfig.Load(unusable);

        config.CycleHotkey.ShouldBe(AppConfig.DefaultCycleHotkey);
        config.Warning.ShouldBeNull();
    }

    [Fact]
    public void DefaultPath_sits_in_the_roaming_application_data_folder()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MControlTray", "config.ini");

        AppConfig.DefaultPath.ShouldBe(expected);
    }

    private void Write(params string[] lines)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllLines(_path, lines);
    }
}
