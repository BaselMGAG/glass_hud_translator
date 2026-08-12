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
    public required string TestKey { get; init; }
    public required string TestingKey { get; init; }
    public required string KeyWorks { get; init; }
    public required string KeyMissing { get; init; }

    /// <summary>
    /// The verdict after a successful test, which now also stores the key. It says both because
    /// the two used to be separate and the gap was invisible: the badge said the key worked while
    /// nothing had been written, and the app went on to report no key at all.
    /// </summary>
    public required string KeyWorksSaved { get; init; }
    public required string KeyRejected { get; init; }
    public required string KeyUnknown { get; init; }
    public required string TestKeysNote { get; init; }
    public required string NoLanes { get; init; }
    public required string NoKeySkipped { get; init; }
    public required string KeysCleared { get; init; }
    public required string ModelsInOrder { get; init; }
    public required string KeyFrom { get; init; }

    /// <summary>Heading above the second and third key box. Formatted with the slot number.</summary>
    public required string KeySlot { get; init; }

    public required string AddAnotherKey { get; init; }

    /// <summary>
    /// Says the one thing that decides whether a second key is worth adding at all: a free tier is
    /// metered per ACCOUNT, so two keys from the same Google project share one allowance and buy
    /// nothing. Without this the feature quietly does nothing for most people who use it.
    /// </summary>
    public required string ExtraKeysNote { get; init; }

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

    /// <summary>The tashkeel switch. Off by default; see <c>AppSettings.Diacritics</c> for why.</summary>
    public required string Diacritics { get; init; }
    public required string DiacriticsNote { get; init; }
    public required string DiacriticsShown { get; init; }
    public required string DiacriticsHidden { get; init; }

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
    public required string OverlayVerticalPosition { get; init; }
    public required string OverlayHorizontalPosition { get; init; }
    public required string OverlayPositionNote { get; init; }
    public required string ResetOverlayPosition { get; init; }
    public required string OverlayCaptureWarning { get; init; }

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

    // ── the live session ───────────────────────────────────────────────────────────────────
    // Everything the translation loop says while the user is in a game. These were English
    // literals inside TranslationSession until the Arabic interface's own overlay started
    // answering in English at the exact moment something had gone wrong.
    public required string NothingCaptured { get; init; }
    public required string TranslationFailed { get; init; }
    public required string AutoWatchOff { get; init; }
    public required string AutoWatchOn { get; init; }
    public required string AutoWatchExpired { get; init; }
    public required string AutoWatchStopped { get; init; }

    /// <summary>One poll threw and was skipped. Status line only - it is not worth the overlay.</summary>
    public required string AutoWatchSkippedFrame { get; init; }

    /// <summary>The session cap, reached. Goes to the overlay: nobody asked for this to happen.</summary>
    public required string AutoWatchReachedLimit { get; init; }

    /// <summary>
    /// The warning the cap exists to deliver, and the reason it is sticky on the overlay rather
    /// than a line in Settings: a player in a fullscreen game reads one of those two.
    /// </summary>
    public required string AutoWatchStillRunning { get; init; }

    /// <summary>Said once, after a long run of empty reads, naming the re-pick hotkey.</summary>
    public required string RegionSeemsWrong { get; init; }

    /// <summary>
    /// The same line again, dropped before it cost anything. Status line only and never the
    /// overlay: the translation the player is reading is still up, and interrupting it to announce
    /// that nothing happened would be the opposite of the saving.
    /// </summary>
    public required string SkippedRepeat { get; init; }

    public required string WatchMode { get; init; }
    public required string WatchModeDialogue { get; init; }
    public required string WatchModeVideo { get; init; }
    public required string WatchModeNote { get; init; }
    public required string WatchModeSetTo { get; init; }
    public required string WatchModeAuto { get; init; }
    public required string WatchModeAutoNote { get; init; }

    /// <summary>Said once per switch. An automatic mode that changes silently cannot be trusted.</summary>
    public required string WatchModeDetected { get; init; }
    public required string ContentDecided { get; init; }
    public required string ContentUndecided { get; init; }
    public required string SecondsBetweenTranslations { get; init; }
    public required string SecondsBetweenAutomatic { get; init; }
    public required string SecondsBetweenNote { get; init; }
    public required string WatchWithoutLimit { get; init; }
    public required string WatchWithoutLimitNote { get; init; }
    public required string NoLimit { get; init; }
    public required string AllowRecording { get; init; }
    public required string AllowRecordingNote { get; init; }
    public required string LearnedPace { get; init; }
    public required string LearnedPaceUnknown { get; init; }
    public required string OutrunningTheFloor { get; init; }
    public required string NoTextInRegion { get; init; }
    public required string TooShortToTranslate { get; init; }
    public required string GameWindowNotFound { get; init; }
    public required string CouldNotSaveFrame { get; init; }
    public required string RegionLayoutChanged { get; init; }
    public required string RegionOffScreenTrimmed { get; init; }

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

    // ── licence ────────────────────────────────────────────────────────────────────────────
    // Shown in Diagnostics rather than only in a readme. Under the AGPL, whoever ends up with a
    // copy is entitled to the source it was built from, and a link in a file on GitHub does not
    // travel with a zip somebody was handed by a friend.
    public required string LicenceSection { get; init; }
    public required string LicenceNote { get; init; }

    // ── region picker ──────────────────────────────────────────────────────────────────────
    public required string SelectRegionTitle { get; init; }
    public required string PickerHintFrozen { get; init; }
    public required string PickerHintPlain { get; init; }
    public required string PickerTooSmall { get; init; }
    public required string OcrReading { get; init; }
    public required string OcrReadNothing { get; init; }
    public required string OcrReads { get; init; }
    public required string OcrFailed { get; init; }

    // ── snip ───────────────────────────────────────────────────────────────────────────────
    public required string SnipTitle { get; init; }
    public required string SnipHint { get; init; }
    public required string SnipCancelled { get; init; }

    // ── capture frame ──────────────────────────────────────────────────────────────────────
    public required string ShowCaptureFrame { get; init; }
    public required string ShowCaptureFrameNote { get; init; }
    public required string FrameAdjustHint { get; init; }
    public required string FrameAdjusted { get; init; }
    public required string FrameNoRegionYet { get; init; }
    public required string FrameWindowsOnly { get; init; }

    // ── toolbar ────────────────────────────────────────────────────────────────────────────
    // Every one of these reaches a tooltip that shows BOTH languages at once, whichever the
    // interface is set to. A toolbar has no text on it - only shapes - so a tooltip in one
    // language leaves somebody guessing: an English speaker helping a friend set it up, or the
    // person the app was built for. The same reasoning as the «Language · اللغة» control, and
    // more strongly, because there is nothing else to read.
    public required string ShowToolbar { get; init; }
    public required string ShowToolbarNote { get; init; }
    public required string ToolbarCanTakeFocus { get; init; }
    public required string ToolbarCanTakeFocusNote { get; init; }
    public required string ToolbarTranslateNow { get; init; }
    public required string ToolbarAutoWatch { get; init; }
    public required string ToolbarSnip { get; init; }
    public required string ToolbarRegion { get; init; }
    public required string ToolbarCaptureFrame { get; init; }
    public required string ToolbarHideOverlay { get; init; }
    public required string ToolbarSettings { get; init; }
    public required string ToolbarMore { get; init; }
    public required string ToolbarLess { get; init; }
    public required string ToolbarWatchMode { get; init; }
    public required string ToolbarDiacritics { get; init; }
    public required string ToolbarPinCorrection { get; init; }
    public required string ToolbarQuit { get; init; }
    public required string ToolbarCollapse { get; init; }
    public required string ToolbarShow { get; init; }
    public required string ToolbarMove { get; init; }

    // ── move mode ──────────────────────────────────────────────────────────────────────────
    // On BOTH surfaces, like everything else: the toolbar for mid-game, Settings for the person
    // who never turned the toolbar on.
    public required string MoveMode { get; init; }
    public required string MoveModeNote { get; init; }
    public required string MoveModeOn { get; init; }
    public required string MoveModeOff { get; init; }
    public required string ToolbarMoveMode { get; init; }
    public required string ToolbarDialect { get; init; }
    public required string ToolbarRecording { get; init; }

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

    // ── health check ───────────────────────────────────────────────────────────────────────
    // One button, twelve questions, plain language. Every string here is the app having the
    // support conversation before it becomes one.
    public required string HealthSection { get; init; }
    public required string HealthNote { get; init; }
    public required string HealthRun { get; init; }
    public required string HealthRunning { get; init; }
    public required string HealthOkWord { get; init; }
    public required string HealthWarningWord { get; init; }
    public required string HealthProblemWord { get; init; }
    public required string HealthNoKeys { get; init; }
    public required string HealthKeysWorking { get; init; }
    public required string HealthKeysRejected { get; init; }
    public required string HealthKeysUnknown { get; init; }
    public required string HealthOcrReady { get; init; }
    public required string HealthOcrMissing { get; init; }
    public required string HealthWholeScreen { get; init; }
    public required string HealthGameFound { get; init; }
    public required string HealthGameNotFound { get; init; }
    public required string HealthGameBlocked { get; init; }
    public required string HealthNoRegion { get; init; }
    public required string HealthRegionSaved { get; init; }
    public required string HealthReadingWell { get; init; }
    public required string HealthReadingPoorly { get; init; }
    public required string HealthScaling { get; init; }
    public required string HealthHardware { get; init; }

    // ── region proposals ───────────────────────────────────────────────────────────────────
    public required string PickerSuggestionHint { get; init; }
    public required string SuggestionLabel { get; init; }
    public required string SuggestionAdopted { get; init; }
    public required string RegionTextBlock { get; init; }
    public required string OcrConfidence { get; init; }

    // ── startup failure ────────────────────────────────────────────────────────────────────
    // Shown on a NORMAL window — decorations, taskbar entry, closable — never on the overlay.
    // The overlay is transparent, has no taskbar button and cannot be focused, so an error
    // written there is exactly what "nothing opens" looks like from the outside.
    public required string StartupFailedTitle { get; init; }
    public required string StartupFailedBody { get; init; }
    public required string StartupFailedLogAt { get; init; }
    public required string StartupFailedClose { get; init; }
    public required string StartupFailedSafeMode { get; init; }

    // ── safe mode ──────────────────────────────────────────────────────────────────────────
    public required string SafeModeBanner { get; init; }

    // ── diagnostic report ──────────────────────────────────────────────────────────────────
    public required string ReportButton { get; init; }
    public required string ReportBuilding { get; init; }
    public required string ReportCopied { get; init; }
    public required string ReportCopiedNoFile { get; init; }

    // ── tray ───────────────────────────────────────────────────────────────────────────────
    // The exit of last resort. The overlay cannot be clicked, the toolbar can be switched off,
    // and Settings can be behind a fullscreen game — the tray is the one control surface Windows
    // itself guarantees stays reachable.
    public required string TrayOpenSettings { get; init; }
    public required string TrayToggleOverlay { get; init; }
    public required string TrayExit { get; init; }

    // ── advanced sections ──────────────────────────────────────────────────────────────────
    // The toolbar's expander owns the concept — simple by default, one control reveals the rest —
    // and Settings consumes the same idea rather than inventing a second one.
    public required string AdvancedSection { get; init; }

    // ── first-run wizard ───────────────────────────────────────────────────────────────────
    public required string WizardWelcome { get; init; }
    public required string WizardWelcomeBody { get; init; }
    public required string WizardStepKey { get; init; }
    public required string WizardKeyWhy { get; init; }
    public required string WizardStepGame { get; init; }
    public required string WizardGameWhy { get; init; }
    public required string WizardGameFound { get; init; }
    public required string WizardStepDone { get; init; }
    public required string WizardDoneWhy { get; init; }
    public required string WizardDrawNow { get; init; }
    public required string WizardLater { get; init; }
    public required string WizardNext { get; init; }
    public required string WizardBack { get; init; }
    public required string WizardSkip { get; init; }

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

    /// <summary>
    /// The display name for a watch mode. Same reasoning as <see cref="RegionName"/>: the enum
    /// member is an identifier, not a word, and interpolating one into a translated sentence is
    /// what produced "حدد dialogue" on three buttons.
    /// </summary>
    public string WatchModeName(Capture.WatchMode mode) => mode switch
    {
        Capture.WatchMode.Video => WatchModeVideo,
        Capture.WatchMode.Auto => WatchModeAuto,
        _ => WatchModeDialogue,
    };

    public string HotkeyDescription(HotkeyAction action) => action switch
    {
        HotkeyAction.PickRegion => HotkeyPickRegion,
        HotkeyAction.TranslateNow => HotkeyTranslateNow,
        HotkeyAction.ToggleAutoWatch => HotkeyToggleAutoWatch,
        HotkeyAction.FlagTranslation => HotkeyFlagTranslation,
        HotkeyAction.ToggleOverlay => HotkeyToggleOverlay,
        HotkeyAction.OpenSettings => HotkeyOpenSettings,
        HotkeyAction.SnipTranslate => HotkeySnipTranslate,
        HotkeyAction.RetryTranslation => HotkeyRetryTranslation,

        // Reached only if an action is added without a name. It is an English enum identifier in
        // an otherwise Arabic window - the exact leak this whole class exists to make impossible -
        // so EveryHotkeyActionHasATranslatedName fails the build's tests rather than a user finding
        // "RetryTranslation" in their hotkey list.
        _ => action.ToString(),
    };

    public required string HotkeyPickRegion { get; init; }
    public required string HotkeyTranslateNow { get; init; }
    public required string HotkeyToggleAutoWatch { get; init; }
    public required string HotkeyFlagTranslation { get; init; }
    public required string HotkeyToggleOverlay { get; init; }
    public required string HotkeyOpenSettings { get; init; }
    public required string HotkeySnipTranslate { get; init; }
    public required string HotkeyRetryTranslation { get; init; }

    // ── saying "no" to a line, and asking for a better answer ────────────────────────────────
    public required string RetryTranslation { get; init; }
    public required string Retrying { get; init; }
    public required string NothingToRetry { get; init; }
    public required string RetriedNote { get; init; }
    public required string EditSourceHeading { get; init; }
    public required string EditSourceNote { get; init; }
    public required string RetranslateEdited { get; init; }
    public required string NothingToEdit { get; init; }
    public required string IgnoredPhrasesHeading { get; init; }
    public required string IgnoredPhrasesNote { get; init; }
    public required string IgnoredPhrasesSaved { get; init; }
    public required string LineIgnored { get; init; }
    public required string ToolbarRetry { get; init; }
    public required string ToolbarReadAgain { get; init; }
    public required string SelfTestButton { get; init; }
    public required string SelfTestNote { get; init; }
    public required string SelfTestDone { get; init; }
    public required string ReadAgain { get; init; }
    public required string ReadAgainNote { get; init; }
    public required string ReadAgainOn { get; init; }
    public required string ReadAgainOff { get; init; }
    public required string ReadAgainUnavailable { get; init; }

    // ── the history tab ──────────────────────────────────────────────────────────────────────
    public required string TabHistory { get; init; }
    public required string HistoryNote { get; init; }
    public required string HistorySearchHint { get; init; }
    public required string HistoryRefresh { get; init; }
    public required string HistoryEmpty { get; init; }
    public required string HistoryShowing { get; init; }
    public required string HistoryPinEdit { get; init; }
    public required string HistoryPinned { get; init; }
    public required string HistoryIgnoreThis { get; init; }
    public required string HistoryIgnoreAdded { get; init; }
    public required string HistoryRetranslate { get; init; }
    public required string HistorySelectFirst { get; init; }
    public required string HistoryNotTranslated { get; init; }

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
            + "roughly 3,500 lines a day - more than a long evening of play.",
        SaveKeys = "Save keys",
        ActiveLanes = "Active lanes",
        TierFree = "FREE TIER",
        TierPaid = "PAID — billed per line",
        TierLocal = "LOCAL",
        PasteKeyHere = "paste your key here",
        TestKey = "Test",
        TestingKey = "Testing...",
        KeyWorks = "Works",
        KeyMissing = "Paste a key first.",
        KeyWorksSaved = "Works — key saved",
        KeyRejected = "This key was refused",
        KeyUnknown = "Could not check right now",
        TestKeysNote =
            "Testing sends one very short line to be translated, so you find out here rather than "
            + "in the middle of a game. It costs one request.",
        NoLanes = "No lanes configured. Translation will fall back to showing the English.",
        NoKeySkipped = "no key, skipped",
        KeysCleared = "All keys cleared. Nothing will be translated until one is entered.",
        ModelsInOrder = "Models tried in order:",
        KeyFrom = "Key from",
        KeySlot = "Key {0}",
        AddAnotherKey = "+ Add another key",
        ExtraKeysNote =
            "You can add up to three keys per provider. They are tried in order before moving on "
            + "to the next provider, so more keys means more lines a day. But a free allowance "
            + "belongs to the ACCOUNT, not the key - two keys from the same Google or Groq account "
            + "share one allowance and buy you nothing. A second key is only worth adding if it "
            + "comes from a second account.",

        WhatAreYouTranslating = "What are you translating?",
        Profile = "Profile",
        Arabic = "Arabic",
        Register = "Style",
        RegisterNote =
            "Modern Standard suits FFXIV's archaic narrative voice. Egyptian lands well for "
            + "merchants and comic relief, and reads as comedy for Elezen nobility.",
        RegisterMsa = "Modern Standard Arabic",
        RegisterEgyptian = "Egyptian Arabic",
        Diacritics = "Show diacritics (tashkeel)",
        DiacriticsNote =
            "Off by default. The models add the short-vowel marks unevenly - the same conversation "
            + "comes back half vowelled and half not, depending on which model answered which line "
            + "- and fully vowelled text reads as scripture or a school book rather than a "
            + "subtitle. This changes what is displayed straight away, including lines already "
            + "translated.",
        DiacriticsShown = "Diacritics will be shown.",
        DiacriticsHidden = "Diacritics will not be shown.",
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
        OverlayVerticalPosition = "Position, top to bottom",
        OverlayHorizontalPosition = "Position, left to right",
        OverlayPositionNote =
            "Move the panel if it sits on top of something you need to see. It moves as you drag, "
            + "so press Preview overlay first and watch it. The position is inside the game's "
            + "window, so it stays put when the game moves or changes monitor.",
        ResetOverlayPosition = "Back to the middle",
        OverlayCaptureWarning =
            "This version of Windows cannot hide the overlay from screen capture. Keep the panel "
            + "away from the area being read, or the app will read its own Arabic back and "
            + "translate that instead of the game.",

        HotkeysNoteWindows =
            "Type a combination such as Ctrl+Shift+T. Modifiers: Ctrl, Shift, Alt, Win. Keys include "
            + "A-Z, 0-9, F1-F24, arrows, Insert/Delete/Home/End, numpad (Num0-Num9) and punctuation. "
            + "F13-F24 are the safest choices - games almost never bind them.",
        HotkeysNoteOther =
            "Global hotkeys are Windows-only. On macOS use the manual buttons below instead.",
        ApplyHotkeys = "Apply hotkeys",
        ResetToDefaults = "Reset to defaults",
        ManualControls = "Manual controls",
        ManualControlsNote = "The same actions, for when a hotkey is unavailable or clashes.",
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

        NothingCaptured = "Nothing was captured. Is the game running in Borderless Windowed mode?",
        TranslationFailed = "Translation failed: {0}",
        AutoWatchOff = "Auto-watch off.",
        AutoWatchOn = "Auto-watch on, {0}. It stops itself after {1} minutes.",
        AutoWatchExpired = "Auto-watch stopped after {0} seconds with no new text.",
        AutoWatchStopped = "Auto-watch stopped: {0}",
        AutoWatchSkippedFrame = "Skipped a frame: {0}",
        AutoWatchReachedLimit =
            "Auto-watch switched itself off after {0} minutes and {1} translations. "
            + "Press the auto-watch key again to carry on.",
        AutoWatchStillRunning =
            "Auto-watch is still on — {0} minutes, {1} translations so far.",
        RegionSeemsWrong =
            "Nothing has been readable in the capture region for a while. If the game's layout "
            + "changed, press {0} to draw the box again.",
        SkippedRepeat = "Same line as before — nothing sent.",

        WatchMode = "What is on screen",
        WatchModeDialogue = "Game dialogue",
        WatchModeVideo = "Video subtitles",
        WatchModeAuto = "Work it out for me",
        WatchModeAutoNote =
            "Watches how the text behaves — whether a line sits there waiting to be clicked, or "
            + "appears and leaves on its own — and switches between the two timings itself. Good "
            + "for a game with cutscenes in it, where the right answer changes mid-session.",
        WatchModeDetected = "Looks like {0} — timings switched.",
        ContentDecided = "running as: {0}",
        ContentUndecided = "still working out what this is",
        WatchModeSetTo = "Auto-watch set to {0}.",
        WatchModeNote =
            "Game dialogue waits for the text to finish appearing, which is right for a line that "
            + "types itself out and stays put. Video subtitles appear whole and leave after a few "
            + "seconds, so that mode checks more often, waits far less, and keeps a minimum gap "
            + "between translations. Video is much more expensive: roughly one request per "
            + "subtitle, so a film is a large part of a day's free allowance.",
        SecondsBetweenTranslations = "Seconds between translations",
        SecondsBetweenAutomatic = "Automatic",
        SecondsBetweenNote =
            "Leave this on Automatic unless you want the pace to be your decision. Higher is "
            + "slower and cheaper; lower is faster, up to the point where the Arabic arrives "
            + "quicker than anyone can read it.",
        WatchWithoutLimit = "Let auto-watch run without a time limit",
        WatchWithoutLimitNote =
            "Off by default. The limit is the only thing that stops a forgotten session spending "
            + "your whole daily allowance — the older 90-second rule cannot help, because it only "
            + "counts time with no new text at all. With this on you still get the warning.",
        NoLimit = "no limit",
        AllowRecording = "Let screen recorders see the overlay",
        AllowRecordingNote =
            "Off by default, which is why the translation is missing from recordings and from the "
            + "Nvidia app. It is hidden from capture so that the app cannot read its own Arabic "
            + "back and translate that instead. Turn it on to record or stream with the "
            + "translation visible — and keep the overlay clear of the capture region if you do.",
        LearnedPace = "Pace learned from what you are watching: a new line about every {0} seconds.",
        LearnedPaceUnknown = "Pace: still measuring.",
        OutrunningTheFloor =
            "Text is changing faster than the gap you have set, so some lines are being skipped.",
        NoTextInRegion = "No text in the capture region. Is a dialogue box actually on screen?",
        TooShortToTranslate = "Only \"{0}\" found — too short to be dialogue.",
        GameWindowNotFound = "Could not find a window for {0}. Is the game running, and not minimised?",
        CouldNotSaveFrame = "Could not save the frame: {0}",
        RegionLayoutChanged =
            "This capture region was drawn on a differently sized window, so it may not line up. "
            + "Press the pick-region hotkey to redraw it.",
        RegionOffScreenTrimmed =
            "Part of the capture region is off screen and was skipped. The display layout has "
            + "changed since it was drawn.",

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

        LicenceSection = "Licence and source",
        LicenceNote =
            "Free software under the GNU Affero General Public License, version 3 or later — use "
            + "it, study it, change it, pass it on. The one condition is that anything built on it "
            + "stays open the same way. The full text is in the LICENSE file beside the program, "
            + "and the source is here:",

        HealthSection = "Health check",
        HealthNote =
            "Checks everything at once — the game window, the capture region, the keys, the text "
            + "recognition — and says what it found in plain words. Run it whenever something "
            + "seems wrong, and before reporting a problem: its output is the report.",
        HealthRun = "Run health check",
        HealthRunning = "Checking… the keys are tested with one real request each.",
        HealthOkWord = "OK",
        HealthWarningWord = "Check",
        HealthProblemWord = "Problem",
        HealthNoKeys =
            "No API key is set, so nothing will be translated. Settings → Providers — a free "
            + "Gemini or Groq key is enough.",
        HealthKeysWorking = "Keys working: {0}",
        HealthKeysRejected =
            "Key refused by: {0}. That key needs to be replaced — retrying will not help.",
        HealthKeysUnknown =
            "Could not verify: {0}. The key may be fine — the provider was unreachable or busy. "
            + "This is not the same as a wrong key.",
        HealthOcrReady = "Text recognition is loaded and ready.",
        HealthOcrMissing =
            "Text recognition could not be loaded, so nothing on screen can be read. If an "
            + "antivirus ran recently, it may have removed files from the app folder — restore "
            + "them from quarantine or unzip the download again.",
        HealthWholeScreen = "Reading the whole screen — no game window needed.",
        HealthGameFound = "Game found: {0}",
        HealthGameNotFound =
            "No window found for \"{0}\". Start the game first, or switch the profile to "
            + "\"anything on screen\".",
        HealthGameBlocked =
            "\"{0}\" is running in exclusive fullscreen, which blocks screen capture. Set the "
            + "game to borderless windowed.",
        HealthNoRegion =
            "No capture region has been drawn for \"{0}\" yet, so the app is guessing where the "
            + "text is. Settings → Translating → Pick dialogue — the picker now suggests "
            + "rectangles it can see text in.",
        HealthRegionSaved = "A capture region is saved for this game.",
        HealthReadingWell = "The capture region reads cleanly — last confidence {0}%.",
        HealthReadingPoorly =
            "The capture region reads poorly — last confidence {0}%. Try drawing the box a "
            + "little wider than the text, not tight against it.",
        HealthScaling = "Display scaling {0}% — detected and handled.",
        HealthHardware = "Machine: {0} cores, {1} GB memory. Translation runs in the cloud, so this is plenty.",

        PickerSuggestionHint =
            "Boxes with text were found — click one to use it, or drag your own.",
        SuggestionLabel = "{0} · {1} words · {2}%",
        SuggestionAdopted = "Suggestion applied — Space tests it, Enter saves it.",
        RegionTextBlock = "Text",
        OcrConfidence = "confidence {0}%",

        StartupFailedTitle = "Glass HUD Translator — could not start",
        StartupFailedBody =
            "The app hit a problem while starting and cannot continue. The details below say "
            + "what went wrong; if you report this, send the whole text and the log file.",
        StartupFailedLogAt = "The same details were written to:",
        StartupFailedClose = "Close",
        StartupFailedSafeMode = "Try safe mode",

        SafeModeBanner =
            "Safe mode: your saved settings were not loaded and nothing you change here will be "
            + "kept. If the app works now, one of the saved settings is the problem — the usual "
            + "one is an overlay positioned onto a screen that is no longer there. Restart "
            + "normally when you are done.",

        ReportButton = "Copy diagnostic report",
        ReportBuilding = "Building the report… the keys are tested with one real request each.",
        ReportCopied =
            "Report copied — paste it into your message. A copy was also saved to {0}.",
        ReportCopiedNoFile = "Report copied — paste it into your message.",

        TrayOpenSettings = "Open Settings",
        TrayToggleOverlay = "Show / hide the translation",
        TrayExit = "Exit Glass HUD Translator",

        AdvancedSection = "Advanced",

        WizardWelcome = "Welcome",
        WizardWelcomeBody =
            "Three quick steps and the first translation is on screen. Everything here can be "
            + "changed later in Settings.",
        WizardStepKey = "The key",
        WizardKeyWhy =
            "Translation runs through a provider, and the key is yours so the free allowance is "
            + "yours. Gemini and Groq are free and need no credit card — the button opens the "
            + "page, copy the key and paste it here. The Test button checks it with one real "
            + "request and saves it when it works.",
        WizardStepGame = "The game",
        WizardGameWhy =
            "A profile carries the game's glossary and where its text sits. Pick yours, or "
            + "\"anything on screen\" for a browser, a video or a game not in the list.",
        WizardGameFound = "Running now: {0}",
        WizardStepDone = "Where the text is",
        WizardDoneWhy =
            "Last step: draw a box around the dialogue once. The screen freezes so nothing "
            + "moves, and the app suggests boxes around text it can already see — click one and "
            + "you are done. After that, one key translates: ",
        WizardDrawNow = "Draw the box now",
        WizardLater = "I will do it later",
        WizardNext = "Next",
        WizardBack = "Back",
        WizardSkip = "Skip setup",

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

        SnipTitle = "Translate once",
        SnipHint =
            "Drag a box around anything on screen. It is translated once, on release, and the "
            + "region you normally watch is left alone.    Esc cancels",
        SnipCancelled = "Nothing selected.",

        ShowCaptureFrame = "Show what is being captured",
        ShowCaptureFrameNote =
            "Draws a thin outline around the capture region so you can see exactly what is being "
            + "read. The outline sits outside the rectangle and is hidden from every screen "
            + "capture, including this app's own, so it can never end up inside the text. Clicks "
            + "pass straight through it until you choose to move it.",
        FrameAdjustHint =
            "Drag the middle to move it, a corner to resize.    Enter saves  ·  Esc cancels",
        FrameAdjusted = "Capture region updated: {0} of the width, {1} of the height.",
        FrameNoRegionYet = "No capture region saved yet for {0}. Draw one first.",
        FrameWindowsOnly = "The capture frame needs Windows.",

        ShowToolbar = "Show the floating toolbar",
        ShowToolbarNote =
            "A small strip of buttons that stays over the game, for everything you would otherwise "
            + "need a hotkey or this window for. It starts with six buttons and one that opens the "
            + "rest, it can be dragged anywhere, and it collapses to a single handle. Hovering a "
            + "button names it in both Arabic and English.",
        ToolbarCanTakeFocus = "Toolbar may take focus",
        ToolbarCanTakeFocusNote =
            "Leave this off. The toolbar normally never pulls the keyboard away from the game, "
            + "which is what you want mid-fight. Turn it on only if the toolbar does not respond "
            + "to clicks at all — on some systems a window that refuses focus also stops receiving "
            + "the mouse, and this is the escape hatch for that.",

        ToolbarTranslateNow = "Translate what is on screen now",
        ToolbarAutoWatch = "Keep watching and translate by itself",
        ToolbarSnip = "Translate one thing once",
        ToolbarRegion = "Choose what gets read",
        ToolbarCaptureFrame = "Show or move the capture outline",
        ToolbarHideOverlay = "Hide or show the translation",
        ToolbarSettings = "Settings",
        ToolbarMore = "More buttons",
        ToolbarLess = "Fewer buttons",
        ToolbarWatchMode = "Switch between game dialogue and video subtitles",
        ToolbarDiacritics = "Show or hide the Arabic vowel marks",
        ToolbarPinCorrection = "Fix the line on screen",
        ToolbarQuit = "Close the app",
        ToolbarCollapse = "Shrink to a handle",
        ToolbarShow = "Open the toolbar",
        ToolbarMove = "Drag here to move the toolbar",

        MoveMode = "Move things",
        MoveModeNote =
            "Unlocks the capture outline and the translation panel so you can drag them where you "
            + "want, and pull a corner of the outline to resize it. While it is on, clicks land on "
            + "them instead of the game and both are outlined so you can see what you have hold "
            + "of. Switch it off and everything is pinned again and clicks pass straight through.",
        MoveModeOn = "Move mode on — drag the outline or the panel. Switch it off to pin them again.",
        MoveModeOff = "Pinned. Clicks pass through to the game again.",
        ToolbarMoveMode = "Move the outline and the panel",
        ToolbarDialect = "Switch between Modern Standard and Egyptian",
        ToolbarRecording = "Let screen recorders see the translation",

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
        HotkeyOpenSettings = "Open Settings",
        HotkeySnipTranslate = "Translate one thing once",
        HotkeyRetryTranslation = "Translate this line again",

        RetryTranslation = "Translate again",
        Retrying = "Translating that line again…",
        NothingToRetry = "Nothing on screen to translate again yet.",
        RetriedNote =
            "Asks for a fresh translation of the line showing now, ignoring the saved one. It costs "
            + "a request, which is the point: the saved answer is what you are trying to replace.",
        EditSourceHeading = "Fix what was read",
        EditSourceNote =
            "If the text recognition misread a word, correct it here and translate again. This "
            + "changes the English that gets sent, not the Arabic that comes back.",
        RetranslateEdited = "Translate the corrected text",
        NothingToEdit = "Nothing has been read yet.",
        IgnoredPhrasesHeading = "Never translate these lines",
        IgnoredPhrasesNote =
            "One line per entry. Anything matching exactly is skipped before anything is sent, so "
            + "it costs nothing at all — useful for a button prompt or a HUD label that keeps "
            + "drifting into the capture region. Small differences in how it is read are allowed "
            + "for. The History tab can add a line here for you.",
        IgnoredPhrasesSaved = "Saved. {0} lines will be skipped.",
        LineIgnored = "That line is on your never-translate list.",
        ToolbarRetry = "Translate again",
        ToolbarReadAgain = "Read hard lines with AI",
        SelfTestButton = "Run full self-test",
        SelfTestNote =
            "Writes a file to your Desktop describing exactly what the app can see right now: every "
            + "window it found, which one it picked as the game and why, where the capture region "
            + "landed, a picture of what it captured, and what the text recognition made of it. "
            + "Run it while the game is on screen, then send the folder. It answers the questions "
            + "that otherwise have to be guessed at.",
        SelfTestDone = "Self-test written to {0} — send the whole folder.",
        ReadAgain = "Let AI read lines the text recognition cannot",
        ReadAgainNote =
            "Off by default. When the text recognition sees writing it cannot make out, this sends "
            + "that part of the screen — as a picture — to the AI to be read instead. It is the same "
            + "free allowance your translations come out of, so it is only used on lines that came "
            + "back unreadable, and never on an empty screen. Nothing is sent while this is off.",
        ReadAgainOn = "On. Unreadable lines will be sent as a picture to be read.",
        ReadAgainOff = "Off. Nothing is sent as a picture.",
        ReadAgainUnavailable = "No provider set up for this yet — it needs a Gemini key.",

        TabHistory = "History",
        HistoryNote =
            "Every line this app has translated, newest first. Search the English, the Arabic or "
            + "the speaker. Correcting a line here fixes it everywhere it appears from now on.",
        HistorySearchHint = "Search…",
        HistoryRefresh = "Refresh",
        HistoryEmpty = "Nothing yet. Translate something and it will appear here.",
        HistoryShowing = "Showing {0} of {1} lines.",
        HistoryPinEdit = "Save this correction",
        HistoryPinned = "Saved. That line will read this way from now on.",
        HistoryIgnoreThis = "Never translate this",
        HistoryIgnoreAdded = "Added to the never-translate list.",
        HistoryRetranslate = "Translate again",
        HistorySelectFirst = "Pick a line from the list first.",
        HistoryNotTranslated = "(not translated)",
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
            "يمنحك Gemini و Groq مفتاحاً دون بطاقة ائتمان، ويغطّيان معاً نحو ٣٬٥٠٠ سطر يومياً — "
            + "أكثر من سهرة لعب طويلة.",
        SaveKeys = "احفظ المفاتيح",
        ActiveLanes = "المسارات الفعّالة",
        TierFree = "مستوى مجاني",
        TierPaid = "مدفوع — يُحاسَب على كل سطر",
        TierLocal = "محلّي",
        PasteKeyHere = "الصق المفتاح هنا",
        TestKey = "جرّبه",
        TestingKey = "جارٍ التجربة...",
        KeyWorks = "يعمل",
        KeyMissing = "الصق مفتاحاً أولاً.",
        // No em dash: it is not in the bundled font (checked against the cmap), and this is the
        // one line that has to be legible - it is the confirmation that setup actually took.
        KeyWorksSaved = "يعمل، وحُفِظ المفتاح",
        KeyRejected = "رُفض هذا المفتاح",
        KeyUnknown = "تعذّر التحقّق الآن",
        TestKeysNote =
            "التجربة ترسل سطراً قصيراً جداً ليُترجَم، فتعرف النتيجة هنا بدل أن تكتشفها وأنت في وسط "
            + "اللعب. وتكلّف طلباً واحداً.",
        NoLanes = "لا توجد مسارات مضبوطة. ستُعرض الإنجليزية بدل الترجمة.",
        NoKeySkipped = "بلا مفتاح، متجاوَز",
        KeysCleared = "مُسحت كل المفاتيح. لن تتم أي ترجمة حتى تُدخل مفتاحاً.",
        ModelsInOrder = "النماذج بالترتيب:",
        KeyFrom = "المفتاح من",
        KeySlot = "المفتاح {0}",
        AddAnotherKey = "+ أضف مفتاحاً آخر",
        ExtraKeysNote =
            "يمكنك إضافة ثلاثة مفاتيح لكل مزوّد. تُجرَّب بالترتيب قبل الانتقال إلى المزوّد التالي، "
            + "فكل مفتاح إضافي يعني سطوراً أكثر في اليوم. لكن الحصة المجانية تخصّ الحساب لا "
            + "المفتاح: مفتاحان من حساب Google أو Groq واحد يتقاسمان حصة واحدة ولا يزيدانك شيئاً. "
            + "فلا فائدة من مفتاح ثانٍ إلا إذا كان من حساب ثانٍ.",

        WhatAreYouTranslating = "ما الذي تترجمه؟",
        Profile = "الملف",
        Arabic = "العربية",
        Register = "أسلوب العربية",
        RegisterNote =
            "الفصحى تناسب الصوت السردي القديم في FFXIV. والمصرية تليق بالتجّار والمواقف الطريفة، "
            + "لكنها تبدو هزلية على لسان نبلاء الإلزِن.",
        RegisterMsa = "العربية الفصحى",
        RegisterEgyptian = "العامية المصرية",
        Diacritics = "إظهار التشكيل",
        DiacriticsNote =
            "مطفأ افتراضياً. النماذج تضيف التشكيل أحياناً وتتركه أحياناً، فيخرج الحوار الواحد نصفه "
            + "مشكولاً ونصفه لا، بحسب النموذج الذي أجاب. والنص المشكول بالكامل يُقرأ كنصّ ديني أو "
            + "كتاب مدرسي لا كترجمة على الشاشة. وتغيير هذا الخيار يظهر أثره فوراً، حتى على السطور "
            + "المترجَمة من قبل.",
        DiacriticsShown = "سيظهر التشكيل.",
        DiacriticsHidden = "لن يظهر التشكيل.",
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
        OverlayVerticalPosition = "الموضع من أعلى إلى أسفل",
        OverlayHorizontalPosition = "الموضع من يمين إلى يسار",
        OverlayPositionNote =
            "حرّك اللوحة إن كانت تغطّي شيئاً تحتاج أن تراه. تتحرّك وأنت تسحب، فاضغط «معاينة الطبقة» "
            + "أولاً وتابعها بعينك. والموضع محسوب داخل نافذة اللعبة، فيبقى كما هو إذا حرّكت اللعبة "
            + "أو نقلتها إلى شاشة أخرى.",
        ResetOverlayPosition = "أعِدها إلى الوسط",
        OverlayCaptureWarning =
            "إصدار ويندوز هذا لا يستطيع إخفاء الطبقة عن التقاط الشاشة. أبعِد اللوحة عن المنطقة "
            + "التي يقرأها البرنامج، وإلا قرأ عربيّته هو وترجمها بدل نصّ اللعبة.",

        HotkeysNoteWindows =
            "اكتب تركيبة مثل Ctrl+Shift+T. المُعدِّلات: Ctrl و Shift و Alt و Win. والمفاتيح تشمل "
            + "A-Z و 0-9 و F1-F24 والأسهم و Insert/Delete/Home/End ولوحة الأرقام (Num0-Num9) "
            + "وعلامات الترقيم. والمفاتيح F13-F24 هي الأكثر أماناً، فالألعاب نادراً ما تستخدمها.",
        HotkeysNoteOther =
            "الاختصارات العامة تعمل على ويندوز فقط. على macOS استخدم الأزرار اليدوية أدناه.",
        ApplyHotkeys = "طبّق الاختصارات",
        ResetToDefaults = "استعادة الافتراضي",
        ManualControls = "تحكّم يدوي",
        ManualControlsNote = "الإجراءات نفسها، لحين تعذّر اختصار أو تعارضه.",
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

        NothingCaptured = "لم يُلتقط شيء. هل اللعبة شغّالة بوضع النافذة بلا إطار؟",
        TranslationFailed = "أخفقت الترجمة: {0}",
        AutoWatchOff = "أُوقفت المتابعة التلقائية.",
        AutoWatchOn = "المتابعة التلقائية شغّالة، {0}. وتتوقّف وحدها بعد {1} دقائق.",
        AutoWatchExpired = "توقّفت المتابعة التلقائية بعد {0} ثانية بلا نص جديد.",
        AutoWatchStopped = "توقّفت المتابعة التلقائية: {0}",
        AutoWatchSkippedFrame = "تخطّى لقطة: {0}",
        AutoWatchReachedLimit =
            "أوقفت المتابعة التلقائية نفسها بعد {0} دقيقة و {1} ترجمة. "
            + "اضغط مفتاح المتابعة مرة أخرى للاستمرار.",
        AutoWatchStillRunning =
            "المتابعة التلقائية ما زالت شغّالة — {0} دقيقة، و {1} ترجمة حتى الآن.",
        RegionSeemsWrong =
            "لم يُقرأ أي نص في منطقة الالتقاط منذ فترة. إن تغيّر شكل واجهة اللعبة، "
            + "اضغط {0} لتحديد المنطقة من جديد.",
        SkippedRepeat = "نفس السطر السابق — لم يُرسَل شيء.",

        WatchMode = "ما الذي على الشاشة",
        WatchModeDialogue = "حوار لعبة",
        WatchModeVideo = "ترجمة فيديو",
        WatchModeAuto = "اكتشفه بنفسك",
        WatchModeAutoNote =
            "يراقب سلوك النص — هل يجلس السطر منتظراً نقرة، أم يظهر ويمضي وحده — ويبدّل بين "
            + "التوقيتَين بنفسه. مفيد للعبة فيها مشاهد سينمائية، حيث تتغيّر الإجابة الصحيحة في "
            + "منتصف الجلسة.",
        WatchModeDetected = "يبدو أنه {0} — بُدِّلت التوقيتات.",
        ContentDecided = "يعمل بوصفه: {0}",
        ContentUndecided = "ما زال يستنتج طبيعة المحتوى",
        WatchModeSetTo = "ضُبطت المتابعة التلقائية على {0}.",
        WatchModeNote =
            "حوار اللعبة ينتظر النص حتى يكتمل ظهوره، وهذا هو الصواب لسطر يُكتب حرفاً حرفاً ثم "
            + "يثبت. أما ترجمة الفيديو فتظهر كاملة وتختفي بعد ثوانٍ، فيتابعها البرنامج أسرع، "
            + "وينتظر أقل بكثير، ويترك فاصلاً أدنى بين ترجمة وأخرى. والفيديو أغلى كثيراً: طلب "
            + "لكل سطر تقريباً، فالفيلم الواحد يلتهم جزءاً كبيراً من حصة اليوم المجانية.",
        SecondsBetweenTranslations = "الثواني بين ترجمة وأخرى",
        SecondsBetweenAutomatic = "تلقائي",
        SecondsBetweenNote =
            "اتركه على «تلقائي» إلا إذا أردت أن يكون الإيقاع قرارك أنت. فالرقم الأكبر أبطأ وأقل "
            + "تكلفة، والأصغر أسرع، إلى الحدّ الذي تصل فيه العربية أسرع مما يستطيع أحد قراءته.",
        WatchWithoutLimit = "دع المتابعة التلقائية تعمل بلا حدّ زمني",
        WatchWithoutLimitNote =
            "مطفأ افتراضياً. الحدّ هو الشيء الوحيد الذي يمنع جلسة منسيّة من استهلاك حصتك اليومية "
            + "كلها — وقاعدة الـ٩٠ ثانية القديمة لا تنفع، لأنها تعدّ الوقت الخالي من أي نص جديد "
            + "فقط. ومع تشغيل هذا الخيار يبقى التنبيه يظهر.",
        NoLimit = "بلا حدّ",
        AllowRecording = "اسمح لبرامج التسجيل برؤية الطبقة",
        AllowRecordingNote =
            "مطفأ افتراضياً، ولهذا لا تظهر الترجمة في التسجيلات ولا في تطبيق Nvidia. فهي مخفيّة "
            + "عن الالتقاط حتى لا يقرأ البرنامج عربيته هو ويترجمها من جديد. شغّله لتسجّل أو تبثّ "
            + "والترجمة ظاهرة — وأبقِ الطبقة بعيدة عن منطقة الالتقاط إن فعلت.",
        LearnedPace = "الإيقاع المستنتَج مما تشاهده: سطر جديد كل {0} ثانية تقريباً.",
        LearnedPaceUnknown = "الإيقاع: ما زال قيد القياس.",
        OutrunningTheFloor =
            "النص يتغيّر أسرع من الفاصل الذي ضبطته، فتُتخطّى بعض السطور.",
        NoTextInRegion = "لا نصّ في منطقة الالتقاط. هل يظهر صندوق حوار على الشاشة فعلاً؟",
        TooShortToTranslate = "لم يُقرأ سوى «{0}» — أقصر من أن يكون حواراً.",
        GameWindowNotFound = "لم يُعثر على نافذة لـ {0}. هل اللعبة شغّالة وغير مصغَّرة؟",
        CouldNotSaveFrame = "تعذّر حفظ الإطار: {0}",
        RegionLayoutChanged =
            "رُسمت منطقة الالتقاط هذه على نافذة بمقاس مختلف، فقد لا تنطبق عليها. اضغط اختصار تحديد "
            + "المنطقة لإعادة رسمها.",
        RegionOffScreenTrimmed =
            "جزء من منطقة الالتقاط خارج الشاشة فتُخُطّي. تغيّر ترتيب الشاشات منذ رُسمت.",

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

        LicenceSection = "الرخصة والشيفرة",
        LicenceNote =
            "برنامج حرّ تحت رخصة جنو AGPL الإصدار الثالث أو ما بعده — استعمله، وادرسه، وغيّره، "
            + "وانقله لغيرك. والشرط الوحيد أن يبقى كل ما يُبنى عليه مفتوحاً بالطريقة نفسها. ونصّ "
            + "الرخصة كاملاً في ملف LICENSE بجوار البرنامج، والشيفرة هنا:",

        HealthSection = "الفحص الشامل",
        HealthNote =
            "يفحص كل شيء دفعة واحدة — نافذة اللعبة، ومنطقة الالتقاط، والمفاتيح، وقراءة النص — "
            + "ويقول ما وجده بكلمات واضحة. شغّله كلما بدا شيء غير طبيعي، وقبل الإبلاغ عن أي "
            + "مشكلة: ما يطبعه هو التقرير نفسه.",
        HealthRun = "افحص الآن",
        HealthRunning = "جارٍ الفحص… تُجرَّب المفاتيح بطلب حقيقي واحد لكل مفتاح.",
        HealthOkWord = "سليم",
        HealthWarningWord = "انتبه",
        HealthProblemWord = "مشكلة",
        HealthNoKeys =
            "لا يوجد أي مفتاح API، فلن يُترجَم شيء. الإعدادات ← المزوّدون — يكفي مفتاح مجاني من "
            + "Gemini أو Groq.",
        HealthKeysWorking = "مفاتيح تعمل: {0}",
        HealthKeysRejected =
            "مفتاح مرفوض عند: {0}. هذا المفتاح يحتاج إلى استبدال — إعادة المحاولة لن تنفع.",
        HealthKeysUnknown =
            "تعذّر التحقّق من: {0}. قد يكون المفتاح سليماً — المزوّد كان بعيد المنال أو مشغولاً. "
            + "هذا ليس كمفتاح خاطئ.",
        HealthOcrReady = "قراءة النص محمّلة وجاهزة.",
        HealthOcrMissing =
            "تعذّر تحميل قراءة النص، فلا يمكن قراءة أي شيء على الشاشة. إن عمل مضادّ فيروسات "
            + "مؤخراً فربما حذف ملفات من مجلد البرنامج — أعدها من الحجر الصحي أو فكّ ضغط "
            + "التنزيل من جديد.",
        HealthWholeScreen = "يقرأ الشاشة كاملة — لا حاجة إلى نافذة لعبة.",
        HealthGameFound = "وُجدت اللعبة: {0}",
        HealthGameNotFound =
            "لا توجد نافذة لـ«{0}». شغّل اللعبة أولاً، أو حوّل الملف إلى «أي شيء على الشاشة».",
        HealthGameBlocked =
            "«{0}» تعمل بملء الشاشة الحصري وهو يمنع التقاط الشاشة. اضبط اللعبة على النافذة "
            + "بلا إطار.",
        HealthNoRegion =
            "لم تُرسم منطقة التقاط لـ«{0}» بعد، فالبرنامج يخمّن مكان النص. الإعدادات ← الترجمة "
            + "← حدد الحوار — صار المحدِّد يقترح مستطيلات يرى فيها نصاً.",
        HealthRegionSaved = "توجد منطقة التقاط محفوظة لهذه اللعبة.",
        HealthReadingWell = "منطقة الالتقاط تُقرأ بوضوح — آخر ثقة {0}٪.",
        HealthReadingPoorly =
            "منطقة الالتقاط تُقرأ بصعوبة — آخر ثقة {0}٪. جرّب رسم الصندوق أوسع قليلاً من النص، "
            + "لا ملاصقاً له.",
        HealthScaling = "تحجيم العرض {0}٪ — مكتشَف ومُعالَج.",
        HealthHardware = "الجهاز: {0} أنوية و {1} غيغابايت ذاكرة. الترجمة تجري سحابياً، فهذا أكثر من كافٍ.",

        PickerSuggestionHint =
            "وُجدت صناديق فيها نص — انقر أحدها لاستخدامه، أو ارسم صندوقك بنفسك.",
        SuggestionLabel = "{0} · {1} كلمة · {2}٪",
        SuggestionAdopted = "طُبّق الاقتراح — Space يجرّبه و Enter يحفظه.",
        RegionTextBlock = "نص",
        OcrConfidence = "الثقة {0}٪",

        StartupFailedTitle = "Glass HUD Translator — تعذّر التشغيل",
        StartupFailedBody =
            "واجه البرنامج مشكلة أثناء التشغيل ولا يستطيع الاستمرار. التفاصيل أدناه تقول ما الذي "
            + "حدث؛ وإن أبلغت عن هذا فأرسل النص كاملاً مع ملف السجل.",
        StartupFailedLogAt = "كُتبت التفاصيل نفسها في:",
        StartupFailedClose = "إغلاق",
        StartupFailedSafeMode = "جرّب الوضع الآمن",

        SafeModeBanner =
            "الوضع الآمن: لم تُحمَّل إعداداتك المحفوظة، ولن يُحفظ أي شيء تغيّره هنا. إن عمل "
            + "البرنامج الآن فأحد الإعدادات المحفوظة هو المشكلة — وأشهرها طبقة ترجمة موضوعة على "
            + "شاشة لم تعد موجودة. أعد التشغيل عادياً حين تنتهي.",

        ReportButton = "انسخ تقرير التشخيص",
        ReportBuilding = "جارٍ إعداد التقرير… تُجرَّب المفاتيح بطلب حقيقي واحد لكل مفتاح.",
        ReportCopied = "نُسخ التقرير — ألصقه في رسالتك. وحُفظت نسخة أيضاً في {0}.",
        ReportCopiedNoFile = "نُسخ التقرير — ألصقه في رسالتك.",

        TrayOpenSettings = "افتح الإعدادات",
        TrayToggleOverlay = "أظهر / أخفِ الترجمة",
        TrayExit = "أغلق Glass HUD Translator",

        AdvancedSection = "متقدّم",

        WizardWelcome = "أهلاً بك",
        WizardWelcomeBody =
            "ثلاث خطوات سريعة وتظهر أول ترجمة على الشاشة. وكل ما هنا يمكن تغييره لاحقاً من "
            + "الإعدادات.",
        WizardStepKey = "المفتاح",
        WizardKeyWhy =
            "الترجمة تمرّ عبر مزوّد، والمفتاح مفتاحك أنت فتكون الحصة المجانية لك. Gemini و Groq "
            + "مجانيان ولا يطلبان بطاقة ائتمان — الزرّ يفتح الصفحة، انسخ المفتاح والصقه هنا. وزرّ "
            + "«جرّبه» يتحقّق منه بطلب حقيقي واحد ويحفظه حين يعمل.",
        WizardStepGame = "اللعبة",
        WizardGameWhy =
            "الملف يحمل مسرد اللعبة وموضع نصّها. اختر لعبتك، أو «أي شيء على الشاشة» لمتصفّح أو "
            + "فيديو أو لعبة ليست في القائمة.",
        WizardGameFound = "يعمل الآن: {0}",
        WizardStepDone = "موضع النص",
        WizardDoneWhy =
            "الخطوة الأخيرة: ارسم صندوقاً حول الحوار مرة واحدة. تتجمّد الشاشة فلا يتحرّك شيء، "
            + "ويقترح البرنامج صناديق حول النص الذي يراه بالفعل — انقر أحدها وتنتهي. وبعدها، "
            + "مفتاح واحد يترجم: ",
        WizardDrawNow = "ارسم الصندوق الآن",
        WizardLater = "سأفعلها لاحقاً",
        WizardNext = "التالي",
        WizardBack = "السابق",
        WizardSkip = "تخطَّ الإعداد",

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

        SnipTitle = "ترجمة مرة واحدة",
        SnipHint =
            "ارسم مستطيلاً حول أي شيء على الشاشة. تُترجَم مرة واحدة بمجرّد رفع الزر، ولا تتأثّر "
            + "المنطقة التي تتابعها عادةً.    Esc يلغي",
        SnipCancelled = "لم تُحدَّد أي منطقة.",

        ShowCaptureFrame = "أظهر ما يجري التقاطه",
        ShowCaptureFrameNote =
            "يرسم إطاراً رفيعاً حول منطقة الالتقاط لترى بالضبط ما الذي يُقرأ. الإطار يقع خارج "
            + "المستطيل ومخفيّ عن كل تصوير للشاشة، بما في ذلك تصوير هذا البرنامج نفسه، فلا يمكن أن "
            + "يدخل داخل النص أبداً. والنقر يمرّ من خلاله إلى اللعبة حتى تختار أنت تحريكه.",
        FrameAdjustHint =
            "اسحب من الوسط لتحريكه، ومن الزاوية لتغيير حجمه.    Enter يحفظ  ·  Esc يلغي",
        FrameAdjusted = "حُدِّثت منطقة الالتقاط: {0} من العرض و {1} من الارتفاع.",
        FrameNoRegionYet = "لا توجد منطقة التقاط محفوظة بعد لـ {0}. حدّد واحدة أولاً.",
        FrameWindowsOnly = "إطار الالتقاط يحتاج إلى ويندوز.",

        ShowToolbar = "أظهر شريط الأدوات العائم",
        ShowToolbarNote =
            "شريط صغير من الأزرار يبقى فوق اللعبة، لكل ما كنت ستحتاج إليه اختصار لوحة مفاتيح أو "
            + "هذه النافذة من أجله. يبدأ بستة أزرار وزرّ يفتح البقية، ويمكن سحبه إلى أي مكان، "
            + "وينكمش إلى مقبض واحد. وعند مرور المؤشّر فوق أي زرّ يظهر اسمه بالعربية والإنجليزية معاً.",
        ToolbarCanTakeFocus = "اسمح لشريط الأدوات بأخذ التركيز",
        ToolbarCanTakeFocusNote =
            "اتركه مُطفأً. الشريط في الوضع الطبيعي لا يسحب لوحة المفاتيح من اللعبة أبداً، وهذا ما "
            + "تريده في وسط قتال. لا تشغّله إلا إذا كان الشريط لا يستجيب للنقر إطلاقاً — فعلى بعض "
            + "الأنظمة النافذة التي ترفض التركيز تتوقّف أيضاً عن استقبال الفأرة، وهذا هو المخرج.",

        ToolbarTranslateNow = "ترجم ما على الشاشة الآن",
        ToolbarAutoWatch = "تابع وترجم تلقائياً",
        ToolbarSnip = "ترجم شيئاً واحداً مرة واحدة",
        ToolbarRegion = "اختر ما الذي يُقرأ",
        ToolbarCaptureFrame = "أظهر إطار الالتقاط أو حرّكه",
        ToolbarHideOverlay = "أخفِ الترجمة أو أظهرها",
        ToolbarSettings = "الإعدادات",
        ToolbarMore = "أزرار أكثر",
        ToolbarLess = "أزرار أقل",
        ToolbarWatchMode = "بدّل بين حوار اللعبة وترجمة الفيديو",
        ToolbarDiacritics = "أظهر التشكيل أو أخفِه",
        ToolbarPinCorrection = "صحّح السطر الظاهر",
        ToolbarQuit = "أغلق البرنامج",
        ToolbarCollapse = "اطوِ الشريط إلى مقبض",
        ToolbarShow = "افتح شريط الأدوات",
        ToolbarMove = "اسحب من هنا لتحريك الشريط",

        MoveMode = "حرّك العناصر",
        MoveModeNote =
            "يفكّ قفل إطار الالتقاط ولوحة الترجمة فتسحبهما حيث تشاء، وتجرّ زاوية الإطار لتغيير "
            + "حجمه. وما دام مشغّلاً فالنقر يقع عليهما لا على اللعبة، ويظهر حولهما إطار لترى ما "
            + "الذي أمسكته. أطفئه فيُثبَّت كل شيء من جديد ويمرّ النقر إلى اللعبة كالمعتاد.",
        MoveModeOn = "وضع التحريك مشغّل — اسحب الإطار أو اللوحة. أطفئه لتثبيتهما من جديد.",
        MoveModeOff = "ثُبِّتت. عاد النقر يمرّ إلى اللعبة.",
        ToolbarMoveMode = "حرّك الإطار واللوحة",
        ToolbarDialect = "بدّل بين الفصحى والمصرية",
        ToolbarRecording = "اسمح لبرامج التسجيل برؤية الترجمة",

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
        HotkeyOpenSettings = "فتح الإعدادات",
        HotkeySnipTranslate = "ترجمة شيء واحد مرة واحدة",
        HotkeyRetryTranslation = "ترجمة هذا السطر من جديد",

        RetryTranslation = "ترجم من جديد",
        Retrying = "يُعيد ترجمة السطر…",
        NothingToRetry = "لا يوجد سطر على الشاشة لإعادة ترجمته بعد.",
        RetriedNote =
            "يطلب ترجمة جديدة للسطر الظاهر الآن متجاهلاً المحفوظة. يكلّف طلباً، وهذا هو المقصود: "
            + "الإجابة المحفوظة هي ما تحاول استبداله.",
        EditSourceHeading = "صحّح ما قُرئ",
        EditSourceNote =
            "إن أخطأت القراءة الضوئية في كلمة، صحّحها هنا ثم أعد الترجمة. هذا يغيّر النصّ "
            + "الإنجليزي المُرسَل، لا العربية العائدة.",
        RetranslateEdited = "ترجم النصّ بعد التصحيح",
        NothingToEdit = "لم يُقرأ شيء بعد.",
        IgnoredPhrasesHeading = "لا تترجم هذه السطور أبداً",
        IgnoredPhrasesNote =
            "سطر واحد لكل إدخال. ما يطابق أحدها يُتخطّى قبل إرسال أي شيء، فلا يكلّف شيئاً على "
            + "الإطلاق — مفيد لعبارة زرّ أو لافتة واجهة تدخل منطقة الالتقاط باستمرار. ويُتسامَح مع "
            + "الفروق الصغيرة في القراءة. وتبويب «السجلّ» يستطيع أن يضيف سطراً هنا نيابةً عنك.",
        IgnoredPhrasesSaved = "حُفظ. سيُتخطّى {0} من السطور.",
        LineIgnored = "هذا السطر في قائمة «لا تترجم» عندك.",
        ToolbarRetry = "ترجم من جديد",
        ToolbarReadAgain = "اقرأ السطور الصعبة بالذكاء الاصطناعي",
        SelfTestButton = "شغّل الفحص الذاتي الكامل",
        SelfTestNote =
            "يكتب ملفاً على سطح المكتب يصف بالضبط ما يراه البرنامج الآن: كل نافذة وجدها، وأيّها "
            + "اختار على أنه اللعبة ولماذا، وأين وقعت منطقة الالتقاط، وصورة لما التقطه، وما فهمته "
            + "القراءة الضوئية منه. شغّله واللعبة على الشاشة ثم أرسل المجلد كاملاً. وهو يجيب عن "
            + "الأسئلة التي لولاه لبقيت تخميناً.",
        SelfTestDone = "كُتب الفحص الذاتي في {0} — أرسل المجلد كاملاً.",
        ReadAgain = "دع الذكاء الاصطناعي يقرأ ما تعجز عنه القراءة الضوئية",
        ReadAgainNote =
            "مُطفأ بشكل افتراضي. حين ترى القراءة الضوئية كتابةً لا تستطيع تمييزها، يُرسَل ذلك الجزء "
            + "من الشاشة — كصورة — إلى الذكاء الاصطناعي ليقرأه بدلاً منها. وهو يستهلك الحصة المجانية "
            + "نفسها التي تخرج منها ترجماتك، فلا يُستخدم إلا على السطور التي عادت غير مقروءة، ولا "
            + "يُستخدم أبداً على شاشة فارغة. ولا يُرسَل شيء ما دام مُطفأً.",
        ReadAgainOn = "يعمل. ستُرسَل السطور غير المقروءة كصورة لتُقرأ.",
        ReadAgainOff = "مُطفأ. لا يُرسَل شيء كصورة.",
        ReadAgainUnavailable = "لا مزوّد مهيّأ لهذا بعد — يحتاج مفتاح Gemini.",

        TabHistory = "السجلّ",
        HistoryNote =
            "كل سطر ترجمه البرنامج، الأحدث أولاً. ابحث في الإنجليزية أو العربية أو اسم المتحدّث. "
            + "وتصحيح سطر هنا يصلحه في كل مرة يظهر فيها من الآن فصاعداً.",
        HistorySearchHint = "ابحث…",
        HistoryRefresh = "تحديث",
        HistoryEmpty = "لا شيء بعد. ترجم شيئاً وسيظهر هنا.",
        HistoryShowing = "يعرض {0} من {1} سطراً.",
        HistoryPinEdit = "احفظ هذا التصحيح",
        HistoryPinned = "حُفظ. سيُقرأ هذا السطر هكذا من الآن فصاعداً.",
        HistoryIgnoreThis = "لا تترجم هذا أبداً",
        HistoryIgnoreAdded = "أُضيف إلى قائمة «لا تترجم».",
        HistoryRetranslate = "ترجم من جديد",
        HistorySelectFirst = "اختر سطراً من القائمة أولاً.",
        HistoryNotTranslated = "(لم تُترجم)",
    };
}
