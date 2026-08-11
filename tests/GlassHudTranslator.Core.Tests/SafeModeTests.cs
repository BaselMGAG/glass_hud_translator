using GlassHudTranslator.Core.Config;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

[Collection(SettingsStaticCollection.Name)]
public class SafeModeTests
{
    [Fact]
    public void SafeModeNeitherReadsNorWritesTheSettingsFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"glasshud-safemode-{Guid.NewGuid():N}.json");

        try
        {
            // A user's real configuration, with a distinctive value in it.
            new AppSettings { OverlayFontSize = 41 }.Save(path);

            AppSettings.SafeMode = true;

            // READ half: the saved value must not come back - the whole point is running on
            // defaults when a saved setting is the suspect.
            var loaded = AppSettings.Load(path);
            Assert.Equal(26, loaded.OverlayFontSize);

            // WRITE half, and the one that would be forgotten: two dozen call sites save on every
            // checkbox click, and any of them writing here would replace the user's configuration
            // with the defaults they were only borrowing.
            loaded.OverlayFontSize = 99;
            loaded.Save(path);

            AppSettings.SafeMode = false;
            Assert.Equal(41, AppSettings.Load(path).OverlayFontSize);
        }
        finally
        {
            AppSettings.SafeMode = false;
            File.Delete(path);
        }
    }
}
