using System.Runtime.Versioning;

namespace GamingTranslatorGlassHUD.Windows;

/// <summary>
/// Session 2 fills these in (CODING_SESSIONS.md). They exist now so the project compiles and CI
/// exercises the Windows TFM from day one, rather than discovering wiring problems on the
/// borrowed laptop.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PlatformStubs
{
    public const string NotImplementedMessage =
        "Win32 layer is implemented in Session 2 - see CODING_SESSIONS.md.";
}
