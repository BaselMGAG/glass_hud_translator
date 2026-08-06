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
