using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Glossary;
using GlassHudTranslator.Core.Profiles;

namespace GlassHudTranslator.App.Views;

/// <summary>
/// Creating and editing a game profile without touching a file.
///
/// <para>
/// A profile used to be three JSON files you copied from a template, which is a perfectly good
/// design for the person who wrote it and an impossible one for the person this app exists for.
/// Everything here maps to a field in <c>profile.json</c>; nothing here can produce a file the
/// loader would reject.
/// </para>
///
/// <para>
/// Laid out as the questions in the order they matter — which window, how it reads, and only then
/// the optional glossary — rather than as the shape of the file it writes. The two fields that
/// actually change translation quality are the window binding and the style, so they come first and
/// the terms table is explicitly marked optional: it is the one part a non-technical user will
/// stall on, and it is the one part they can safely skip and fill in later with the correction
/// hotkey.
/// </para>
/// </summary>
public sealed class ProfileEditorWindow : Window
{
    private readonly UiText _text;
    private readonly ProfileLibrary _library;
    private readonly GameProfile? _existing;

    private readonly TextBox _name;
    private readonly TextBox _windowTitle;
    private readonly TextBox _processName;
    private readonly ComboBox _style;
    private readonly TextBox _customStyle;
    private readonly CheckBox _speakerNames;
    private readonly StackPanel _terms;
    private readonly StackPanel _windowList;
    private readonly TextBlock _error;

    /// <summary>The saved profile's id, or null when the user cancelled.</summary>
    public string? SavedId { get; private set; }

    /// <summary>True when this created a profile rather than editing one, so the caller can chain
    /// straight into picking a capture region — which is the step that actually makes it work.</summary>
    public bool WasCreated { get; private set; }

    /// <summary>Renders this window for the documentation, in whichever language it was built in.</summary>
    public void SaveSnapshot(string path)
    {
        if (Content is Control root)
            WindowSnapshot.Save(root, Width, Height, _text.IsRightToLeft, path);
    }

    public ProfileEditorWindow(UiText text, ProfileLibrary library, GameProfile? existing = null)
    {
        _text = text;
        _library = library;
        _existing = existing;

        Title = existing is null
            ? _text.NewGameTitle
            : string.Format(_text.EditGameTitle, existing.DisplayName);

        Width = 720;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        FlowDirection = _text.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        FontFamily = _text.IsRightToLeft ? Fonts.Arabic : FontFamily.Default;

        _name = new TextBox { Text = existing?.DisplayName ?? "", Width = 320 };

        // Machine text: a window title is whatever the program set, and an executable name is not
        // a word. Mirroring either would reorder it.
        _windowTitle = new TextBox
        {
            Text = string.Join(", ", existing?.WindowTitles ?? []),
            Width = 320,
            FlowDirection = FlowDirection.LeftToRight,
        };
        _processName = new TextBox
        {
            Text = string.Join(", ", existing?.ProcessNames ?? []),
            Width = 320,
            FlowDirection = FlowDirection.LeftToRight,
        };

        _style = new ComboBox { ItemsSource = StyleLabels(), Width = 320 };
        _style.SelectedIndex = SelectedStyleIndex(existing?.StyleHint);

        _customStyle = new TextBox
        {
            Text = StylePreset.Match(existing?.StyleHint) is null ? existing?.StyleHint ?? "" : "",
            Width = 460,
            AcceptsReturn = true,
            Height = 70,
            TextWrapping = TextWrapping.Wrap,

            // The hint is written into an English system prompt, so it is composed left-to-right
            // even when the interface around it is mirrored.
            FlowDirection = FlowDirection.LeftToRight,
        };

        _speakerNames = new CheckBox
        {
            Content = _text.SpeakerNamesLabel,
            IsChecked = existing?.HasSpeakerNames ?? true,
        };

        _terms = new StackPanel { Spacing = 6 };
        _windowList = new StackPanel { Spacing = 4 };
        _error = new TextBlock
        {
            Text = "",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#f28b82")),
        };

        foreach (var term in existing?.Glossary.Terms.Where(t => t.Type != "_comment") ?? [])
            _terms.Children.Add(TermRow(term.En, term.Ar));

        _style.SelectionChanged += (_, _) => UpdateCustomVisibility();

        Content = BuildBody();
        UpdateCustomVisibility();
        RefreshWindows();
    }

    // ── layout ────────────────────────────────────────────────────────────────────────────

    private Control BuildBody()
    {
        var stack = new StackPanel { Spacing = 12, Margin = new Thickness(24, 20) };

        stack.Children.Add(Row(_text.GameName, _name));
        stack.Children.Add(Note(_text.GameNameNote));

        stack.Children.Add(Section(_text.WhichWindow));
        stack.Children.Add(Note(_text.WhichWindowNote));
        stack.Children.Add(_windowList);
        stack.Children.Add(Button(_text.RefreshWindowList, RefreshWindows));
        stack.Children.Add(Row(_text.WindowTitleLabel, _windowTitle, 130));
        stack.Children.Add(Row(_text.ProgramNameLabel, _processName, 130));

        stack.Children.Add(Section(_text.HowItReads));
        stack.Children.Add(Note(_text.HowItReadsNote));
        stack.Children.Add(_style);
        stack.Children.Add(_customStyle);
        stack.Children.Add(Note(_text.StyleCustomNote));

        stack.Children.Add(_speakerNames);
        stack.Children.Add(Note(_text.SpeakerNamesNote));

        stack.Children.Add(Section(_text.TermsSection));
        stack.Children.Add(Note(_text.TermsNote));
        stack.Children.Add(_terms);
        stack.Children.Add(Button(_text.AddTerm, () => _terms.Children.Add(TermRow("", ""))));

        if (_existing is not null && _library.OriginOf(_existing.Id) is not ProfileOrigin.User)
            stack.Children.Add(Note(_text.BundledProfileNote));

        stack.Children.Add(_error);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0),
        };
        buttons.Children.Add(Button(_text.SaveProfile, Save));
        buttons.Children.Add(Button(_text.CancelProfile, Close));
        stack.Children.Add(buttons);

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1e1e1e")),
            Child = new ScrollViewer { Content = stack },
        };
    }

    /// <summary>
    /// The list of open windows. Buttons rather than a dropdown so the program name is readable
    /// beside each title — two windows called "Settings" are told apart only by what owns them.
    /// </summary>
    private void RefreshWindows()
    {
        _windowList.Children.Clear();

        var open = PlatformServices.ListOpenWindows();
        if (open.Count == 0)
        {
            _windowList.Children.Add(Note(PlatformServices.IsWindows
                ? _text.NoWindowsListed
                : _text.WindowListWindowsOnly));
            return;
        }

        foreach (var window in open.Take(40))
        {
            var caption = window.ProcessName.Length > 0
                ? $"{window.Title}   —   {window.ProcessName}"
                : window.Title;

            var button = new Button
            {
                Content = caption,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,

                // The caption is entirely machine text - a title the program chose and an
                // executable name - so it is not mirrored with the rest of the window.
                FlowDirection = FlowDirection.LeftToRight,
            };

            button.Click += (_, _) =>
            {
                _windowTitle.Text = window.Title;
                _processName.Text = window.ProcessName;
                if (_name.Text is null or "") _name.Text = window.Title;
            };

            _windowList.Children.Add(button);
        }

        _windowList.Children.Add(Button(_text.AnythingOnScreen, () =>
        {
            // Deliberately clears both. A profile with no binding at all is measured against the
            // whole screen, which is what a browser or a video player needs.
            _windowTitle.Text = "";
            _processName.Text = "";
        }));
    }

    private void UpdateCustomVisibility()
    {
        var custom = _style.SelectedIndex == StylePreset.All.Count;
        _customStyle.IsVisible = custom;
    }

    // ── saving ────────────────────────────────────────────────────────────────────────────

    private void Save()
    {
        var name = _name.Text?.Trim() ?? "";
        if (name.Length == 0)
        {
            _error.Text = _text.NameRequired;
            return;
        }

        var draft = new GameProfileDraft
        {
            ExistingId = _existing?.Id,
            DisplayName = name,
            WindowTitles = Split(_windowTitle.Text),
            ProcessNames = Split(_processName.Text),
            SourceLanguage = _existing?.SourceLanguage ?? "eng",
            StyleHint = ChosenStyleHint(),
            HasSpeakerNames = _speakerNames.IsChecked == true,
            Terms = CollectedTerms(),
        };

        try
        {
            SavedId = _library.Save(draft);
            WasCreated = _existing is null;
            Close();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException)
        {
            // A read-only data directory, a name the filesystem refuses, a full disk. Reported in
            // the window rather than thrown: the user is mid-edit and their typing is still here.
            _error.Text = $"{_text.ProfileSaveFailed} {e.Message}";
        }
    }

    private string? ChosenStyleHint()
    {
        if (_style.SelectedIndex == StylePreset.All.Count)
        {
            var custom = _customStyle.Text?.Trim();
            return string.IsNullOrWhiteSpace(custom) ? StylePreset.Plain.Hint : custom;
        }

        var index = Math.Clamp(_style.SelectedIndex, 0, StylePreset.All.Count - 1);
        return StylePreset.All[index].Hint;
    }

    private IReadOnlyList<GlossaryTerm> CollectedTerms() => _terms.Children
        .OfType<Panel>()
        .Select(row => row.Children.OfType<TextBox>().ToList())
        .Where(boxes => boxes.Count >= 2)
        .Select(boxes => new GlossaryTerm(
            boxes[0].Text?.Trim() ?? "", boxes[1].Text?.Trim() ?? "", "term", []))
        .Where(t => t.En.Length > 0 && t.Ar.Length > 0)
        .ToList();

    private int SelectedStyleIndex(string? hint)
    {
        if (_existing is null) return 0;

        var matched = StylePreset.Match(hint);
        if (matched is null) return StylePreset.All.Count;   // hand-written, so Custom

        var index = StylePreset.All.ToList().FindIndex(p => p.Id == matched.Id);
        return index < 0 ? 0 : index;
    }

    private IReadOnlyList<string> StyleLabels() =>
    [
        _text.StylePlain, _text.StyleEpic, _text.StyleModern, _text.StyleComic,
        _text.StyleTechnical, _text.StyleCustom,
    ];

    private static string[] Split(string? value) => (value ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ── small helpers ─────────────────────────────────────────────────────────────────────

    private Control TermRow(string english, string arabic)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        // The source term is the game's own text, so it stays left-to-right; the Arabic does not.
        row.Children.Add(new TextBox
        {
            Text = english, Width = 240, Watermark = _text.TermEnglish,
            FlowDirection = FlowDirection.LeftToRight,
        });
        row.Children.Add(new TextBox { Text = arabic, Width = 240, Watermark = _text.TermArabic });

        var remove = new Button { Content = _text.RemoveTerm };
        remove.Click += (_, _) => _terms.Children.Remove(row);
        row.Children.Add(remove);

        return row;
    }

    private static TextBlock Section(string text) => new()
    {
        Text = text, FontSize = 15, FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 12, 0, 0),
        Foreground = new SolidColorBrush(Color.Parse("#8ab4f8")),
    };

    private TextBlock Note(string text) => new()
    {
        Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap,
        LineSpacing = _text.IsRightToLeft ? 5 : 3,
        Foreground = new SolidColorBrush(Color.Parse("#c8ccd0")),
    };

    private static Button Button(string text, Action onClick)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Control Row(string label, Control control, double labelWidth = 90) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 12,
        Children =
        {
            new TextBlock
            {
                Text = label, Width = labelWidth, VerticalAlignment = VerticalAlignment.Center,
            },
            control,
        },
    };
}
