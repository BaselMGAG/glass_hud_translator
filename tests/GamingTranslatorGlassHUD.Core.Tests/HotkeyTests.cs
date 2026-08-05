using GamingTranslatorGlassHUD.Core.Config;
using GamingTranslatorGlassHUD.Core.Platform;
using Xunit;

namespace GamingTranslatorGlassHUD.Core.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Shift+T", HotkeyModifiers.Control | HotkeyModifiers.Shift, "T")]
    [InlineData("ctrl+alt+F13", HotkeyModifiers.Control | HotkeyModifiers.Alt, "F13")]
    [InlineData("Win+Shift+Num5", HotkeyModifiers.Windows | HotkeyModifiers.Shift, "NUM5")]
    [InlineData("Control + Shift + PageUp", HotkeyModifiers.Control | HotkeyModifiers.Shift, "PAGEUP")]
    public void ParsesCombinations(string text, HotkeyModifiers modifiers, string key)
    {
        var hotkey = Hotkey.TryParse(text);

        Assert.NotNull(hotkey);
        Assert.Equal(modifiers, hotkey.Modifiers);
        Assert.Equal(key, hotkey.Key);
        Assert.True(hotkey.IsValid);
    }

    [Fact]
    public void RoundTripsThroughItsDisplayForm()
    {
        var hotkey = Hotkey.TryParse("Ctrl+Shift+T");

        Assert.Equal("Ctrl+Shift+T", hotkey!.ToString());
        Assert.Equal(hotkey, Hotkey.TryParse(hotkey.ToString()));
    }

    [Fact]
    public void ModifierlessHotkeyIsRejected()
    {
        // Binding a bare key would swallow it from the game entirely.
        Assert.False(Hotkey.TryParse("T")!.IsValid);
    }

    [Fact]
    public void UnknownKeyIsRejected()
    {
        var hotkey = Hotkey.TryParse("Ctrl+Shift+NotAKey");

        Assert.Equal(0u, hotkey!.VirtualKey);
        Assert.False(hotkey.IsValid);
    }

    [Theory]
    [InlineData("A", 0x41u)]
    [InlineData("F13", 0x7Cu)]
    [InlineData("Num0", 0x60u)]
    [InlineData("PageDown", 0x22u)]
    [InlineData("[", 0xDBu)]
    public void MapsToWin32VirtualKeys(string key, uint expected)
    {
        Assert.Equal(expected, new Hotkey(HotkeyModifiers.Control, key).VirtualKey);
    }

    [Fact]
    public void DefaultsAvoidFunctionKeysThatGamesBind()
    {
        foreach (var (_, hotkey) in DefaultHotkeys.All)
        {
            Assert.True(hotkey.IsValid);
            Assert.DoesNotMatch("^F([1-9]|1[0-2])$", hotkey.Key);
        }
    }

    [Fact]
    public void EmptyInputParsesToNothing()
    {
        Assert.Null(Hotkey.TryParse(null));
        Assert.Null(Hotkey.TryParse("   "));
        Assert.Null(Hotkey.TryParse("Ctrl+Shift"));
    }
}

public class AppSettingsTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");

    [Fact]
    public void UnsetHotkeysFallBackToDefaults()
    {
        var settings = new AppSettings();

        Assert.Equal(DefaultHotkeys.All[HotkeyAction.TranslateNow],
            settings.HotkeyFor(HotkeyAction.TranslateNow));
    }

    [Fact]
    public void CustomHotkeysSurviveARoundTrip()
    {
        var path = TempPath();
        try
        {
            var settings = new AppSettings();
            settings.SetHotkey(HotkeyAction.TranslateNow, new Hotkey(HotkeyModifiers.Alt, "F13"));
            settings.OverlayFontSize = 32;
            settings.Save(path);

            var reloaded = AppSettings.Load(path);

            Assert.Equal("Alt+F13", reloaded.HotkeyFor(HotkeyAction.TranslateNow).ToString());
            Assert.Equal(32, reloaded.OverlayFontSize);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ConflictingBindingsAreReported()
    {
        // Two actions on one combination means one silently never fires.
        var settings = new AppSettings();
        settings.SetHotkey(HotkeyAction.TranslateNow, new Hotkey(HotkeyModifiers.Control, "T"));
        settings.SetHotkey(HotkeyAction.ToggleAutoWatch, new Hotkey(HotkeyModifiers.Control, "T"));

        var conflicts = settings.FindConflicts();

        Assert.Contains(HotkeyAction.TranslateNow, conflicts);
        Assert.Contains(HotkeyAction.ToggleAutoWatch, conflicts);
    }

    [Fact]
    public void DistinctBindingsReportNoConflict()
    {
        Assert.Empty(new AppSettings().FindConflicts());
    }

    [Fact]
    public void CorruptSettingsFileFallsBackToDefaultsRatherThanCrashing()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "{ this is not json");

            Assert.Equal(26, AppSettings.Load(path).OverlayFontSize);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AutoWatchExpiryDefaultsToNinetySeconds()
    {
        // Not optional: a toggle left on during an AFK is the main way to leak API quota.
        Assert.Equal(90, new AppSettings().AutoWatchExpirySeconds);
    }
}
