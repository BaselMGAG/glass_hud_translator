using GlassHudTranslator.Core.Platform;
using GlassHudTranslator.Core.Regions;

namespace GlassHudTranslator.Core.Config;

public enum UiLanguage
{
    English,
    Arabic,
}

/// <summary>
/// Every user-facing string in the app, in one place, in both languages.
///
/// <para>
/// The app translates games for people who read Arabic more comfortably than English, and until
/// this existed its own interface was English-only - so the person who most needs the tool was the
/// one least able to set it up. English stays the default because that is what the screenshots and
/// the documentation show, but the whole interface can be switched, including its direction.
/// </para>
///
/// <para>
/// Deliberately a class of <c>required</c> properties rather than a key/value dictionary: a missing
/// translation is then a compile error rather than a string that silently falls back to English
/// somewhere nobody looks. Adding a string means the compiler makes you write both.
/// </para>
/// </summary>
public sealed class UiText
{
    public required UiLanguage Language { get; init; }

    /// <summary>True for a right-to-left interface, not only right-to-left text.</summary>
    public bool IsRightToLeft => Language == UiLanguage.Arabic;

    // ── window and tabs ────────────────────────────────────────────────────────────────────
    public required string WindowTitle { get; init; }
    public required string TabProviders { get; init; }
    public required string TabTranslating { get; init; }
    public required string TabOverlay { get; init; }
    public required string TabHotkeys { get; init; }
    public required string TabDiagnostics { get; init; }

    // ── language ───────────────────────────────────────────────────────────────────────────
    public required string InterfaceLanguage { get; init; }
    public required string LanguageChanged { get; init; }

    // ── providers ──────────────────────────────────────────────────────────────────────────
    public required string DevBuildWarning { get; init; }
    public required string ProvidersIntro { get; init; }
    public required string FreeProvidersNote { get; init; }
    public required string SaveKeys { get; init; }
    public required string ActiveLanes { get; init; }
    public required string TierFree { get; init; }
    public required string TierPaid { get; init; }
    public required string TierLocal { get; init; }
    public required string PasteKeyHere { get; init; }
    public required string NoLanes { get; init; }
    public required string NoKeySkipped { get; init; }
    public required string KeysCleared { get; init; }
    public required string ModelsInOrder { get; init; }
    public required string KeyFrom { get; init; }

    // ── translating ────────────────────────────────────────────────────────────────────────
    public required string WhatAreYouTranslating { get; init; }
    public required string Profile { get; init; }
    public required string Arabic { get; init; }

    /// <summary>
    /// Label for the MSA/Egyptian choice. Named after <c>ArabicRegister</c>, which is what it sets,
    /// but deliberately not <em>worded</em> that way in either language: "register" is a linguist's
    /// term, and its literal Arabic equivalent (المستوى اللغوي) reads as a grading scale rather
    /// than a choice between two dialects. The label says what the two options are instead.
    /// </summary>
    public required string Register { get; init; }
    public required string RegisterNote { get; init; }
    public required string RegisterMsa { get; init; }
    public required string RegisterEgyptian { get; init; }
    public required string CaptureRegions { get; init; }
    public required string RegionsNote { get; init; }
    public required string PickRegion { get; init; }
    public required string RegionDialogue { get; init; }
    public required string RegionSubtitle { get; init; }
    public required string RegionQuest { get; init; }
    public required string Corrections { get; init; }
    public required string CorrectionsNote { get; init; }
    public required string CorrectedArabic { get; init; }
    public required string PinCorrection { get; init; }

    // ── overlay ────────────────────────────────────────────────────────────────────────────
    public required string FontSize { get; init; }
    public required string PanelOpacity { get; init; }
    public required string OverlayNote { get; init; }
    public required string PreviewOverlay { get; init; }
    public required string ShowHideOverlay { get; init; }

    // ── hotkeys ────────────────────────────────────────────────────────────────────────────
    public required string HotkeysNoteWindows { get; init; }
    public required string HotkeysNoteOther { get; init; }
    public required string ApplyHotkeys { get; init; }
    public required string ResetToDefaults { get; init; }
    public required string ManualControls { get; init; }
    public required string ManualControlsNote { get; init; }
    public required string TranslateNow { get; init; }
    public required string ToggleAutoWatch { get; init; }

    // ── diagnostics ────────────────────────────────────────────────────────────────────────
    public required string RouterLog { get; init; }
    public required string RouterLogNote { get; init; }
    public required string TestTranslation { get; init; }
    public required string Refresh { get; init; }
    public required string QuotaToday { get; init; }
    public required string Cache { get; init; }
    public required string Entries { get; init; }
    public required string Corrected { get; init; }
    public required string Hits { get; init; }
    public required string Translating { get; init; }

    // ── game profiles ──────────────────────────────────────────────────────────────────────
    public required string AddGame { get; init; }
    public required string EditProfile { get; init; }
    public required string DeleteProfile { get; init; }
    public required string NewGameTitle { get; init; }
    public required string EditGameTitle { get; init; }
    public required string GameName { get; init; }
    public required string GameNameNote { get; init; }
    public required string WhichWindow { get; init; }
    public required string WhichWindowNote { get; init; }
    public required string RefreshWindowList { get; init; }
    public required string NoWindowsListed { get; init; }
    public required string WindowListWindowsOnly { get; init; }
    public required string AnythingOnScreen { get; init; }
    public required string WindowTitleLabel { get; init; }
    public required string ProgramNameLabel { get; init; }
    public required string HowItReads { get; init; }
    public required string HowItReadsNote { get; init; }
    public required string StylePlain { get; init; }
    public required string StyleEpic { get; init; }
    public required string StyleModern { get; init; }
    public required string StyleComic { get; init; }
    public required string StyleTechnical { get; init; }
    public required string StyleCustom { get; init; }
    public required string StyleCustomNote { get; init; }
    public required string SpeakerNamesLabel { get; init; }
    public required string SpeakerNamesNote { get; init; }
    public required string TermsSection { get; init; }
    public required string TermsNote { get; init; }
    public required string TermEnglish { get; init; }
    public required string TermArabic { get; init; }
    public required string AddTerm { get; init; }
    public required string RemoveTerm { get; init; }
    public required string SaveProfile { get; init; }
    public required string CancelProfile { get; init; }
    public required string NameRequired { get; init; }
    public required string ProfileCreated { get; init; }
    public required string ProfileUpdated { get; init; }
    public required string ProfileDeleted { get; init; }
    public required string ConfirmDeleteProfile { get; init; }
    public required string ConfirmDelete { get; init; }
    public required string KeepProfile { get; init; }
    public required string ProfileReadOnly { get; init; }
    public required string BundledProfileNote { get; init; }
    public required string NextPickRegion { get; init; }
    public required string ProfileSaveFailed { get; init; }

    // ── updates ────────────────────────────────────────────────────────────────────────────
    public required string UpdateAvailable { get; init; }
    public required string UpdateDownloadFile { get; init; }
    public required string UpdateSteps { get; init; }
    public required string UpdateKeepsYourSetup { get; init; }
    public required string OpenDownloadPage { get; init; }
    public required string DismissUpdate { get; init; }
    public required string Updates { get; init; }
    public required string CheckForUpdatesLabel { get; init; }
    public required string CheckForUpdatesNote { get; init; }
    public required string CheckNow { get; init; }
    public required string CheckingForUpdates { get; init; }
    public required string UpToDate { get; init; }
    public required string UpdateCheckUnavailable { get; init; }
    public required string UpdateCheckOffline { get; init; }
    public required string UpdateCheckDisabled { get; init; }
    public required string DevelopmentBuildNoUpdates { get; init; }

    // ── region picker ──────────────────────────────────────────────────────────────────────
    public required string SelectRegionTitle { get; init; }
    public required string PickerHintFrozen { get; init; }
    public required string PickerHintPlain { get; init; }
    public required string PickerTooSmall { get; init; }
    public required string OcrReading { get; init; }
    public required string OcrReadNothing { get; init; }
    public required string OcrReads { get; init; }
    public required string OcrFailed { get; init; }

    // ── status messages ────────────────────────────────────────────────────────────────────
    public required string ProfileNoteWindowed { get; init; }
    public required string ProfileNoteScreen { get; init; }
    public required string ProfileSwitchedRegionRestored { get; init; }
    public required string ProfileSwitchedNoRegion { get; init; }
    public required string KeysSaved { get; init; }
    public required string RegionSaved { get; init; }
    public required string RegisterSetTo { get; init; }
    public required string TestResult { get; init; }
    public required string NothingToCorrect { get; init; }
    public required string EditThenPin { get; init; }
    public required string CorrectionPinned { get; init; }
    public required string RegionUnchanged { get; init; }
    public required string OverlayShown { get; init; }
    public required string OverlayHidden { get; init; }
    public required string AllHotkeysRegistered { get; init; }
    public required string HotkeyConflict { get; init; }
    public required string HotkeyInvalid { get; init; }
    public required string TestFailed { get; init; }
    public required string DiagnosticsFailed { get; init; }
    public required string CaptureWindowsOnly { get; init; }
    public required string SelectionOffScreen { get; init; }

    /// <summary>
    /// The display name for a capture region. The stored names are English identifiers - they are
    /// dictionary keys in the region store and in every saved profile - so they cannot simply be
    /// translated at the source. Interpolating one straight into a sentence is what produced
    /// "حدد dialogue" on a button: half a translated interface, which reads as an unfinished build.
    /// Anything unrecognised falls through unchanged rather than being dropped.
    /// </summary>
    public string RegionName(string region) => region switch
    {
        RegionProfile.Names.Dialogue => RegionDialogue,
        RegionProfile.Names.Subtitle => RegionSubtitle,
        RegionProfile.Names.Quest => RegionQuest,
        _ => region,
    };

    public string HotkeyDescription(HotkeyAction action) => action switch
    {
        HotkeyAction.PickRegion => HotkeyPickRegion,
        HotkeyAction.TranslateNow => HotkeyTranslateNow,
        HotkeyAction.ToggleAutoWatch => HotkeyToggleAutoWatch,
        HotkeyAction.FlagTranslation => HotkeyFlagTranslation,
        HotkeyAction.ToggleOverlay => HotkeyToggleOverlay,
        _ => action.ToString(),
    };

    public required string HotkeyPickRegion { get; init; }
    public required string HotkeyTranslateNow { get; init; }
    public required string HotkeyToggleAutoWatch { get; init; }
    public required string HotkeyFlagTranslation { get; init; }
    public required string HotkeyToggleOverlay { get; init; }

    public static UiText For(UiLanguage language) => language == UiLanguage.Arabic ? Ar : En;

    /// <summary>Both languages, for a language picker. Names are in their own language.</summary>
    public static IReadOnlyList<(UiLanguage Language, string Name)> Choices { get; } =
    [
        (UiLanguage.English, "English"),
        (UiLanguage.Arabic, "العربية"),
    ];

    public static readonly UiText En = new()
    {
        Language = UiLanguage.English,

        WindowTitle = "Glass HUD Translator",
        TabProviders = "Providers",
        TabTranslating = "Translating",
        TabOverlay = "Overlay",
        TabHotkeys = "Hotkeys",
        TabDiagnostics = "Diagnostics",

        InterfaceLanguage = "Language",
        LanguageChanged = "Interface language changed.",

        DevBuildWarning =
            "Development build. Capture replays recorded frames, hotkeys are inactive, and API "
            + "keys are stored in PLAINTEXT. Windows uses BitBlt, RegisterHotKey and DPAPI.",
        ProvidersIntro =
            "Bring your own key. Nothing is embedded in this app, and lanes are tried top to "
            + "bottom - so the free tiers answer first and a paid provider only sees the lines they "
            + "could not. A lane with no key is switched off and costs nothing.",
        FreeProvidersNote =
            "Gemini and Groq both issue a key without a credit card, and between them cover "
            + "roughly 15,000 lines a day - more than a full day of play.",
        SaveKeys = "Save keys",
        ActiveLanes = "Active lanes",
        TierFree = "FREE TIER",
        TierPaid = "PAID — billed per line",
        TierLocal = "LOCAL",
        PasteKeyHere = "paste your key here",
        NoLanes = "No lanes configured. Translation will fall back to showing the English.",
        NoKeySkipped = "no key, skipped",
        KeysCleared = "All keys cleared. Nothing will be translated until one is entered.",
        ModelsInOrder = "Models tried in order:",
        KeyFrom = "Key from",

        WhatAreYouTranslating = "What are you translating?",
        Profile = "Profile",
        Arabic = "Arabic",
        Register = "Style",
        RegisterNote =
            "Modern Standard suits FFXIV's archaic narrative voice. Egyptian lands well for "
            + "merchants and comic relief, and reads as comedy for Elezen nobility.",
        RegisterMsa = "Modern Standard Arabic",
        RegisterEgyptian = "Egyptian Arabic",
        CaptureRegions = "Capture regions",
        RegionsNote =
            "Games often draw narrative text in more than one place — a dialogue box, a subtitle bar, "
            + "a quest window — so each gets its own rectangle. Each profile keeps its own set, so "
            + "switching between a game and the desktop does not lose either.",
        PickRegion = "Pick {0}",
        RegionDialogue = "dialogue",
        RegionSubtitle = "subtitle",
        RegionQuest = "quest",
        Corrections = "Corrections",
        CorrectionsNote = "Correct the line currently on the overlay. The correction is pinned "
                          + "and always wins over the model in future.",
        CorrectedArabic = "corrected Arabic",
        PinCorrection = "Pin correction",

        FontSize = "Font size",
        PanelOpacity = "Panel opacity",
        OverlayNote =
            "Both apply live. Never set a fixed line height on the overlay: too tight and the marks "
            + "that hang below the baseline are clipped, which turns ي into ى — a different letter.",
        PreviewOverlay = "Preview overlay",
        ShowHideOverlay = "Show / hide overlay",

        HotkeysNoteWindows =
            "Type a combination such as Ctrl+Shift+T. Modifiers: Ctrl, Shift, Alt, Win. Keys include "
            + "A-Z, 0-9, F1-F24, arrows, Insert/Delete/Home/End, numpad (Num0-Num9) and punctuation. "
            + "F13-F24 are the safest choices - games almost never bind them.",
        HotkeysNoteOther =
            "Global hotkeys are Windows-only. On macOS use the manual buttons below instead.",
        ApplyHotkeys = "Apply hotkeys",
        ResetToDefaults = "Reset to defaults",
        ManualControls = "Manual controls",
        ManualControlsNote = "The same five actions, for when a hotkey is unavailable or clashes.",
        TranslateNow = "Translate now",
        ToggleAutoWatch = "Toggle auto-watch",

        RouterLog = "Router log",
        RouterLogNote = "A model disappearing upstream shows up here, by name. So does a "
                        + "provider being rate limited, and a line that fell back to English.",
        TestTranslation = "Test translation",
        Refresh = "Refresh",
        QuotaToday = "Quota today:",
        Cache = "Cache:",
        Entries = "entries",
        Corrected = "corrected",
        Hits = "hits",
        Translating = "Translating...",

        AddGame = "+ Add a game",
        EditProfile = "Edit",
        DeleteProfile = "Delete",
        NewGameTitle = "Add a game",
        EditGameTitle = "Edit {0}",
        GameName = "Name",
        GameNameNote = "Whatever you want to see in the list. It does not have to match anything.",
        WhichWindow = "Which window?",
        WhichWindowNote =
            "Pick the game from the list of what is open. The region you drag is then measured "
            + "against that window, so moving the window does not break it. Start the game first if "
            + "it is not listed.",
        RefreshWindowList = "Refresh the list",
        NoWindowsListed = "Nothing found. Start the game, then refresh.",
        WindowListWindowsOnly =
            "Listing open windows works on Windows only. Type part of the window title instead.",
        AnythingOnScreen = "Anything on screen (no particular window)",
        WindowTitleLabel = "Window title",
        ProgramNameLabel = "Program",
        HowItReads = "How does it read?",
        HowItReadsNote =
            "This is the single biggest thing you can set. It tells the model what the writing is "
            + "meant to sound like, which is what stops a solemn story being translated in the "
            + "register of a shop sign.",
        StylePlain = "Plain and accurate",
        StyleEpic = "Serious fantasy",
        StyleModern = "Modern and casual",
        StyleComic = "Funny",
        StyleTechnical = "Menus and numbers",
        StyleCustom = "Describe it myself",
        StyleCustomNote = "One or two sentences, in English — it goes to the model, not to you.",
        SpeakerNamesLabel = "The game shows a speaker's name above the text",
        SpeakerNamesNote =
            "True for most story games with dialogue boxes. Turn it off for subtitles, menus, or "
            + "anything where the first line is part of the sentence rather than a name.",
        TermsSection = "Names and terms (optional)",
        TermsNote =
            "Proper nouns you want spelled the same way every time — characters, places, factions. "
            + "You can skip this entirely and add them as you play: pressing the correction hotkey "
            + "on a bad line pins the fix for good.",
        TermEnglish = "In the game",
        TermArabic = "In Arabic",
        AddTerm = "+ Add a term",
        RemoveTerm = "Remove",
        SaveProfile = "Save",
        CancelProfile = "Cancel",
        NameRequired = "Give it a name first.",
        ProfileCreated = "'{0}' added. Now show it where the text is.",
        ProfileUpdated = "'{0}' saved.",
        ProfileDeleted = "'{0}' deleted.",
        ConfirmDeleteProfile =
            "Delete '{0}'? Its capture regions, terms and settings go with it. This cannot be undone.",
        ConfirmDelete = "Delete it",
        KeepProfile = "Keep it",
        ProfileReadOnly =
            "This one reads whatever is on screen and cannot be edited or removed — it is the "
            + "fallback that works on anything.",
        BundledProfileNote =
            "This profile ships with the app. Your changes are saved separately and survive updates; "
            + "the original stays underneath.",
        NextPickRegion = "Show it where the text is",
        ProfileSaveFailed = "Could not save the profile:",

        UpdateAvailable = "Version {0} is available. You have {1}.",
        UpdateDownloadFile = "On the page that opens, under Assets, download:",
        UpdateSteps =
            "1. Unzip it anywhere — no installer, and no administrator rights.   "
            + "2. Run GlassHudTranslator.exe from the new folder.   "
            + "3. Delete the old folder once it works. Windows will warn about an unrecognised app "
            + "the first time: More info → Run anyway.",
        UpdateKeepsYourSetup =
            "Your API keys, settings, capture regions and translation cache are kept — they are "
            + "stored under your Windows account, not in the app folder. Anything you edited inside "
            + "profiles/ or data/models.json does not carry over, so copy those across if you "
            + "changed them.",
        OpenDownloadPage = "Open the download page",
        DismissUpdate = "Not now",
        Updates = "Updates",
        CheckForUpdatesLabel = "Check for updates",
        CheckForUpdatesNote =
            "Asks GitHub once a day whether a newer release exists, and never downloads or installs "
            + "anything by itself. It is the only request this app makes that is not a translation. "
            + "Nothing is sent with it: no identifier, no usage, no API key.",
        CheckNow = "Check now",
        CheckingForUpdates = "Checking...",
        UpToDate = "You have the latest version ({0}).",
        UpdateCheckUnavailable = "Could not reach GitHub. It will try again later.",
        UpdateCheckOffline = "Last checked {0}.",
        UpdateCheckDisabled = "Update checking is off. Nothing is sent to GitHub.",
        DevelopmentBuildNoUpdates =
            "Running from source, so there is no released version to compare against.",

        SelectRegionTitle = "Select the {0} region",
        PickerHintFrozen =
            "Drag a box over the {0} text. This is a frozen screenshot, so nothing will move while "
            + "you aim.    Space tests the OCR  ·  Enter saves  ·  Esc cancels",
        PickerHintPlain = "Drag a box over the {0} area.    Enter saves  ·  Esc cancels",
        PickerTooSmall = "Too small - drag across the whole text box.",
        OcrReading = "Reading...",
        OcrReadNothing = "OCR read nothing here. Try covering more of the text, or less of the border.",
        OcrReads = "OCR reads:",
        OcrFailed = "OCR failed:",

        ProfileNoteWindowed =
            "Active: {0}. Carries its own glossary ({1} terms) and measures the capture region "
            + "against that application's window, so the region survives the window being moved.",
        ProfileNoteScreen =
            "Active: {0}. No window of its own — the capture region is measured against the whole "
            + "screen. This is what you want for a browser, a PDF or a video player. Move the "
            + "window and you will need to pick the region again.",
        ProfileSwitchedRegionRestored = "Switched to '{0}'. Its saved capture region is back.",
        ProfileSwitchedNoRegion =
            "Switched to '{0}'. No region picked for it yet — press Ctrl+Shift+R.",
        KeysSaved = "{0} key(s) saved. Lanes without one are skipped.",
        RegionSaved = "Saved '{0}' as {1} x {2} of the client rect.",
        RegisterSetTo = "Arabic style set to {0}.",
        TestResult = "{0}/{1} in {2} ms ({3})",
        NothingToCorrect = "Nothing on the overlay to correct yet.",
        EditThenPin = "Edit the text above, then press Pin correction.",
        CorrectionPinned = "Correction pinned. It will be used for this line from now on.",
        RegionUnchanged = "Region '{0}' unchanged.",
        OverlayShown = "Overlay shown.",
        OverlayHidden = "Overlay hidden. Translation carries on in the background.",
        AllHotkeysRegistered = "All {0} hotkeys registered.",
        HotkeyConflict = "Two actions share a combination: {0}. One of them would never fire.",
        HotkeyInvalid = "'{0}' is not a usable combination for {1}. It needs at least one modifier "
                        + "and a known key.",
        TestFailed = "Test failed:",
        DiagnosticsFailed = "Could not read diagnostics:",
        CaptureWindowsOnly = "(screen capture is Windows-only)",
        SelectionOffScreen = "(selection is off-screen)",

        HotkeyPickRegion = "Pick the capture region",
        HotkeyTranslateNow = "Translate what is on screen now",
        HotkeyToggleAutoWatch = "Toggle auto-watch",
        HotkeyFlagTranslation = "Correct the current translation",
        HotkeyToggleOverlay = "Show / hide the overlay",
    };

    public static readonly UiText Ar = new()
    {
        Language = UiLanguage.Arabic,

        WindowTitle = "Glass HUD Translator",
        TabProviders = "المزوّدون",
        TabTranslating = "الترجمة",
        TabOverlay = "الطبقة",
        TabHotkeys = "الاختصارات",
        TabDiagnostics = "التشخيص",

        InterfaceLanguage = "لغة الواجهة",
        LanguageChanged = "تم تغيير لغة الواجهة.",

        DevBuildWarning =
            "نسخة تطوير. الالتقاط يعيد تشغيل إطارات مسجّلة، والاختصارات معطّلة، ومفاتيح الواجهة "
            + "البرمجية تُخزَّن بنص صريح. أما على ويندوز فيُستخدم BitBlt و RegisterHotKey و DPAPI.",
        ProvidersIntro =
            "مفتاحك أنت. لا مفتاح مدمج في هذا البرنامج، وتُجرَّب المسارات من الأعلى إلى الأسفل — "
            + "فيجيب المجاني أولاً، ولا يرى المدفوع إلا السطور التي عجز عنها. وأي مسار بلا مفتاح "
            + "يبقى مُعطَّلاً ولا يكلّف شيئاً.",
        FreeProvidersNote =
            "يمنحك Gemini و Groq مفتاحاً دون بطاقة ائتمان، ويغطّيان معاً نحو ١٥٬٠٠٠ سطر يومياً — "
            + "أكثر من يوم كامل من اللعب.",
        SaveKeys = "احفظ المفاتيح",
        ActiveLanes = "المسارات الفعّالة",
        TierFree = "مستوى مجاني",
        TierPaid = "مدفوع — يُحاسَب على كل سطر",
        TierLocal = "محلّي",
        PasteKeyHere = "الصق المفتاح هنا",
        NoLanes = "لا توجد مسارات مضبوطة. ستُعرض الإنجليزية بدل الترجمة.",
        NoKeySkipped = "بلا مفتاح، متجاوَز",
        KeysCleared = "مُسحت كل المفاتيح. لن تتم أي ترجمة حتى تُدخل مفتاحاً.",
        ModelsInOrder = "النماذج بالترتيب:",
        KeyFrom = "المفتاح من",

        WhatAreYouTranslating = "ما الذي تترجمه؟",
        Profile = "الملف",
        Arabic = "العربية",
        Register = "أسلوب العربية",
        RegisterNote =
            "الفصحى تناسب الصوت السردي القديم في FFXIV. والمصرية تليق بالتجّار والمواقف الطريفة، "
            + "لكنها تبدو هزلية على لسان نبلاء الإلزِن.",
        RegisterMsa = "العربية الفصحى",
        RegisterEgyptian = "العامية المصرية",
        CaptureRegions = "مناطق الالتقاط",
        RegionsNote =
            "كثيراً ما ترسم الألعاب نصّها السردي في أكثر من موضع — صندوق حوار، وشريط ترجمة، ونافذة "
            + "مهام — فلكلٍّ منها مستطيله. ولكل ملف مجموعته الخاصة، فالتنقّل بين لعبة وسطح المكتب "
            + "لا يفقدك أياً منهما.",
        PickRegion = "حدّد منطقة {0}",
        RegionDialogue = "الحوار",
        RegionSubtitle = "الترجمة",
        RegionQuest = "المهمة",
        Corrections = "التصحيحات",
        CorrectionsNote = "صحّح السطر المعروض على الطبقة الآن. يُثبَّت التصحيح ويتقدّم على النموذج "
                          + "دائماً فيما بعد.",
        CorrectedArabic = "النص العربي المصحَّح",
        PinCorrection = "ثبّت التصحيح",

        FontSize = "حجم الخط",
        PanelOpacity = "شفافية اللوحة",
        OverlayNote =
            "كلاهما يسري فوراً. لا تحدّد ارتفاعاً ثابتاً لسطر الطبقة أبداً: فالضيق يقصّ العلامات "
            + "المتدلّية تحت السطر، فتتحوّل ي إلى ى — وهو حرف آخر.",
        PreviewOverlay = "معاينة الطبقة",
        ShowHideOverlay = "إظهار / إخفاء الطبقة",

        HotkeysNoteWindows =
            "اكتب تركيبة مثل Ctrl+Shift+T. المُعدِّلات: Ctrl و Shift و Alt و Win. والمفاتيح تشمل "
            + "A-Z و 0-9 و F1-F24 والأسهم و Insert/Delete/Home/End ولوحة الأرقام (Num0-Num9) "
            + "وعلامات الترقيم. والمفاتيح F13-F24 هي الأكثر أماناً، فالألعاب نادراً ما تستخدمها.",
        HotkeysNoteOther =
            "الاختصارات العامة تعمل على ويندوز فقط. على macOS استخدم الأزرار اليدوية أدناه.",
        ApplyHotkeys = "طبّق الاختصارات",
        ResetToDefaults = "استعادة الافتراضي",
        ManualControls = "تحكّم يدوي",
        ManualControlsNote = "الإجراءات الخمسة نفسها، لحين تعذّر اختصار أو تعارضه.",
        TranslateNow = "ترجم الآن",
        ToggleAutoWatch = "تشغيل/إيقاف المتابعة",

        RouterLog = "سجلّ الموجِّه",
        RouterLogNote = "اختفاء نموذج لدى المزوّد يظهر هنا بالاسم. وكذلك بلوغ حدّ الطلبات، "
                        + "والسطر الذي عاد إلى الإنجليزية.",
        TestTranslation = "جرّب الترجمة",
        Refresh = "تحديث",
        QuotaToday = "حصة اليوم:",
        Cache = "الذاكرة المؤقتة:",
        Entries = "مدخلاً",
        Corrected = "مصحَّحاً",
        Hits = "إصابة",
        Translating = "جارٍ الترجمة...",

        AddGame = "+ أضف لعبة",
        EditProfile = "تعديل",
        DeleteProfile = "حذف",
        NewGameTitle = "إضافة لعبة",
        EditGameTitle = "تعديل {0}",
        GameName = "الاسم",
        GameNameNote = "ما تريد أن تراه في القائمة. لا يلزم أن يطابق شيئاً.",
        WhichWindow = "أيّ نافذة؟",
        WhichWindowNote =
            "اختر اللعبة من قائمة النوافذ المفتوحة حاليًا. سيحفظ البرنامج مكان النص بالنسبة "
            + "لنافذة اللعبة، لذلك لو حرّكت النافذة لن تحتاج لتحديد المنطقة من جديد. وإن لم تجد "
            + "اللعبة فشغّلها أولاً.",
        RefreshWindowList = "حدّث القائمة",
        NoWindowsListed = "لم يُعثر على شيء. شغّل اللعبة ثم حدّث القائمة.",
        WindowListWindowsOnly =
            "سرد النوافذ المفتوحة يعمل على ويندوز فقط. اكتب جزءاً من عنوان النافذة بدلاً من ذلك.",
        AnythingOnScreen = "أي شيء على الشاشة (بلا نافذة محدّدة)",
        WindowTitleLabel = "عنوان النافذة",
        ProgramNameLabel = "البرنامج",
        HowItReads = "كيف يُقرأ نصّها؟",
        HowItReadsNote =
            "هذا أهم ما تضبطه هنا. فهو يخبر النموذج بالنبرة المقصودة، وهو ما يمنع أن تُترجَم قصة "
            + "جادّة بأسلوب لافتة محل.",
        StylePlain = "بسيط ودقيق",
        StyleEpic = "فانتازيا جادّة",
        StyleModern = "عصري ودارج",
        StyleComic = "طريف",
        StyleTechnical = "قوائم وأرقام",
        StyleCustom = "أصفها بنفسي",
        StyleCustomNote = "جملة أو جملتان بالإنجليزية — فهي تذهب إلى النموذج لا إليك.",
        SpeakerNamesLabel = "تعرض اللعبة اسم المتحدّث فوق النص",
        SpeakerNamesNote =
            "صحيح في معظم الألعاب القصصية ذات صناديق الحوار. أوقفه في الترجمات المصاحبة والقوائم، "
            + "أو حيثما كان السطر الأول جزءاً من الجملة لا اسماً.",
        TermsSection = "الأسماء والمصطلحات (اختياري)",
        TermsNote =
            "الأسماء التي تريدها مكتوبة بالطريقة نفسها في كل مرة — شخصيات وأماكن وجماعات. ويمكنك "
            + "تخطّي هذا تماماً وإضافتها أثناء اللعب: فضغط اختصار التصحيح على سطر خاطئ يثبّت "
            + "التصحيح نهائياً.",
        TermEnglish = "في اللعبة",
        TermArabic = "بالعربية",
        AddTerm = "+ أضف مصطلحاً",
        RemoveTerm = "إزالة",
        SaveProfile = "احفظ",
        CancelProfile = "إلغاء",
        NameRequired = "ضع لها اسماً أولاً.",
        ProfileCreated = "أُضيفت «{0}». والآن دلّها على موضع النص.",
        ProfileUpdated = "حُفظت «{0}».",
        ProfileDeleted = "حُذفت «{0}».",
        ConfirmDeleteProfile =
            "أتحذف «{0}»؟ ستذهب معها مناطق الالتقاط والمصطلحات والإعدادات. ولا يمكن التراجع.",
        ConfirmDelete = "احذفها",
        KeepProfile = "أبقِها",
        ProfileReadOnly =
            "هذه تقرأ أي شيء على الشاشة، ولا تُعدَّل ولا تُحذف — فهي البديل الذي يعمل مع كل شيء.",
        BundledProfileNote =
            "هذا الملف يأتي مع البرنامج. وتُحفظ تعديلاتك منفصلة عنه فتبقى بعد التحديثات، ويبقى "
            + "الأصل تحتها.",
        NextPickRegion = "دلّها على موضع النص",
        ProfileSaveFailed = "تعذّر حفظ الملف:",

        UpdateAvailable = "صدرت النسخة {0}، ولديك {1}.",
        UpdateDownloadFile = "في الصفحة التي ستُفتح، تحت Assets، نزّل الملف:",
        UpdateSteps =
            "١. فُكّ ضغطه في أي مكان — بلا مثبِّت وبلا صلاحيات مدير.   "
            + "٢. شغّل GlassHudTranslator.exe من المجلد الجديد.   "
            // Two Latin runs either side of an arrow reverse in a mirrored paragraph, so the
            // instruction would read "Run anyway" first. An Arabic connective between them fixes
            // the order in the words themselves rather than relying on the layout.
            + "٣. احذف المجلد القديم بعد أن يعمل. وسيحذّرك ويندوز من برنامج غير معروف أول مرة، "
            + "فاضغط «More info» ثم «Run anyway».",
        UpdateKeepsYourSetup =
            "تبقى مفاتيحك وإعداداتك ومناطق الالتقاط والذاكرة المؤقتة كما هي — فهي محفوظة في حسابك "
            + "على ويندوز لا في مجلد البرنامج. أما ما عدّلته داخل profiles/ أو data/models.json فلا "
            + "ينتقل معك، فانسخه إن كنت قد غيّرته.",
        OpenDownloadPage = "افتح صفحة التنزيل",
        DismissUpdate = "ليس الآن",
        Updates = "التحديثات",
        CheckForUpdatesLabel = "التحقّق من التحديثات",
        CheckForUpdatesNote =
            "يسأل GitHub مرة كل يوم إن كانت هناك نسخة أحدث، ولا ينزّل ولا يثبّت شيئاً من تلقاء "
            + "نفسه. وهو الطلب الوحيد الذي يرسله البرنامج ولا يكون ترجمة. ولا يُرسَل معه شيء: لا "
            + "معرّف ولا بيانات استخدام ولا مفتاح.",
        CheckNow = "تحقّق الآن",
        CheckingForUpdates = "جارٍ التحقّق...",
        UpToDate = "لديك أحدث نسخة ({0}).",
        UpdateCheckUnavailable = "تعذّر الوصول إلى GitHub. سيُعاد المحاولة لاحقاً.",
        UpdateCheckOffline = "آخر تحقّق: {0}.",
        UpdateCheckDisabled = "التحقّق من التحديثات مُعطَّل. ولا يُرسَل شيء إلى GitHub.",
        DevelopmentBuildNoUpdates = "تعمل النسخة من المصدر، فلا إصدار منشور تُقارَن به.",

        SelectRegionTitle = "حدّد منطقة {0}",
        PickerHintFrozen =
            "ارسم مستطيلاً فوق نص {0}. هذه لقطة مجمّدة، فلن يتحرّك شيء أثناء التحديد.    "
            + "Space يختبر القراءة  ·  Enter يحفظ  ·  Esc يلغي",
        PickerHintPlain = "ارسم مستطيلاً فوق منطقة {0}.    Enter يحفظ  ·  Esc يلغي",
        PickerTooSmall = "صغير جداً — امتدّ بالتحديد على صندوق النص كاملاً.",
        OcrReading = "جارٍ القراءة...",
        OcrReadNothing = "لم يقرأ المحرّك شيئاً هنا. جرّب تغطية نص أكثر، أو حدود أقل.",
        OcrReads = "القراءة الضوئية:",
        OcrFailed = "أخفقت القراءة الضوئية:",

        ProfileNoteWindowed =
            "المُفعَّل: {0}. له مسرده الخاص ({1} مصطلحاً)، وتُقاس منطقة الالتقاط على نافذة ذلك "
            + "التطبيق، فتبقى المنطقة صالحة إذا حُرّكت النافذة.",
        ProfileNoteScreen =
            "المُفعَّل: {0}. بلا نافذة خاصة — تُقاس منطقة الالتقاط على الشاشة كاملة. وهذا ما تريده "
            + "للمتصفّح أو ملفات PDF أو مشغّل الفيديو. وإن حرّكت النافذة فستحتاج إلى إعادة التحديد.",
        ProfileSwitchedRegionRestored = "انتقلت إلى «{0}». عادت منطقة الالتقاط المحفوظة له.",
        ProfileSwitchedNoRegion = "انتقلت إلى «{0}». لم تُحدَّد له منطقة بعد — اضغط Ctrl+Shift+R.",
        KeysSaved = "حُفظ {0} من المفاتيح. والمسارات بلا مفتاح متجاوَزة.",
        RegionSaved = "حُفظت «{0}» بنسبة {1} × {2} من مساحة النافذة.",
        RegisterSetTo = "ضُبط أسلوب العربية على {0}.",
        TestResult = "{0}/{1} خلال {2} جزء من الثانية ({3})",
        NothingToCorrect = "لا يوجد على الطبقة ما يُصحَّح بعد.",
        EditThenPin = "عدّل النص أعلاه ثم اضغط «ثبّت التصحيح».",
        CorrectionPinned = "ثُبِّت التصحيح. سيُستخدم لهذا السطر من الآن فصاعداً.",
        RegionUnchanged = "لم تتغيّر منطقة «{0}».",
        OverlayShown = "الطبقة ظاهرة.",
        OverlayHidden = "أُخفيت الطبقة. وتستمر الترجمة في الخلفية.",
        AllHotkeysRegistered = "سُجِّلت الاختصارات كلها ({0}).",
        HotkeyConflict = "إجراءان يتشاركان التركيبة نفسها: {0}. أحدهما لن يعمل أبداً.",
        HotkeyInvalid = "التركيبة «{0}» غير صالحة لـ {1}. تحتاج مُعدِّلاً واحداً على الأقل ومفتاحاً "
                        + "معروفاً.",
        TestFailed = "أخفقت التجربة:",
        DiagnosticsFailed = "تعذّرت قراءة التشخيص:",
        CaptureWindowsOnly = "(التقاط الشاشة على ويندوز فقط)",
        SelectionOffScreen = "(التحديد خارج الشاشة)",

        HotkeyPickRegion = "تحديد منطقة الالتقاط",
        HotkeyTranslateNow = "ترجمة ما هو على الشاشة الآن",
        HotkeyToggleAutoWatch = "تشغيل/إيقاف المتابعة التلقائية",
        HotkeyFlagTranslation = "تصحيح الترجمة الحالية",
        HotkeyToggleOverlay = "إظهار / إخفاء الطبقة",
    };
}
