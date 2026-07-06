using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CodeEditor.Services;
using CodeEditor.ViewModels;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Indentation;
using ICSharpCode.AvalonEdit.Indentation.CSharp;
using ICSharpCode.AvalonEdit.Rendering;

namespace CodeEditor.Views;

/// <summary>
/// Hosts the AvalonEdit editor for a <see cref="DocumentViewModel"/>. A single
/// instance is reused across tab switches (TabControl content templating), so all
/// wiring reacts to <see cref="FrameworkElement.DataContextChanged"/>.
/// </summary>
public partial class EditorView : UserControl
{
    private readonly SearchHighlightRenderer _searchHighlightRenderer = new();
    private readonly DiagnosticSquiggleRenderer _squiggleRenderer = new();
    private readonly SemanticHighlightColorizer _semanticColorizer = new();

    private EditorOptionsViewModel? _observedOptions;
    private DocumentViewModel? _observedDocument;
    private ToolTip? _diagnosticToolTip;
    private CompletionWindow? _completionWindow;
    private CancellationTokenSource? _completionCts;
    private OverloadInsightWindow? _insightWindow;
    private int _hoverToken;

    public EditorView()
    {
        InitializeComponent();

        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.EnableRectangularSelection = true;

        // Theme-aware colors that are not bindable from XAML.
        Editor.TextArea.SetResourceReference(TextArea.SelectionBrushProperty, "Brush.Editor.Selection");
        Editor.TextArea.TextView.SetResourceReference(TextView.CurrentLineBackgroundProperty, "Brush.Editor.CurrentLine");
        Editor.TextArea.SelectionBorder = null;
        UpdateCaretBrush();

        Editor.TextArea.TextView.BackgroundRenderers.Add(_searchHighlightRenderer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_squiggleRenderer);
        // Added after construction, so it stays behind the highlighting colorizer
        // (which TextEditor inserts at index 0) and semantic colors win.
        Editor.TextArea.TextView.LineTransformers.Add(_semanticColorizer);

        DataContextChanged += OnDataContextChanged;
        Editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        Editor.TextArea.SelectionChanged += OnSelectionChanged;
        Editor.TextArea.TextView.MouseHover += OnTextViewMouseHover;
        Editor.TextArea.TextView.MouseHoverStopped += (_, _) =>
        {
            _hoverToken++;
            CloseDiagnosticToolTip();
        };
        Editor.TextArea.TextEntered += OnTextEntered;
        Editor.TextArea.PreviewKeyDown += OnTextAreaPreviewKeyDown;
        // Repaint token colors and the caret when the syntax palette is recolored
        // on a theme change (startup recolor runs before this view exists).
        SyntaxHighlightingTheme.Recolored += OnSyntaxHighlightingRecolored;
        // Fires once the Document binding has applied after a tab switch — the safe
        // moment to honor a navigation that was requested while opening the file.
        Editor.DocumentChanged += (_, _) => ApplyPendingNavigation();
    }

    /// <summary>Moves keyboard focus into the text area (e.g. after the find panel closes).</summary>
    public void FocusEditor() => Editor.TextArea.Focus();

    private void OnSyntaxHighlightingRecolored(object? sender, EventArgs e)
    {
        // The shared HighlightingColor instances were mutated; a redraw re-reads
        // their foregrounds. Also refresh the caret, whose accent color may have changed.
        UpdateCaretBrush();
        Editor.TextArea.TextView.Redraw();
    }

    /// <summary>
    /// The caret draws in the accent color. Caret.CaretBrush is not a dependency
    /// property, so this re-resolves on tab switches (covering theme changes too).
    /// </summary>
    private void UpdateCaretBrush()
        => Editor.TextArea.Caret.CaretBrush = TryFindResource("Brush.Accent") as System.Windows.Media.Brush;

    private DocumentViewModel? ViewModel => DataContext as DocumentViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        ObserveOptions(viewModel.Options);
        ObserveDocument(viewModel);
        ApplyTabWidth(viewModel.Options.TabWidth);

        Editor.TextArea.IndentationStrategy = viewModel.Language.Id == "csharp"
            ? new CSharpIndentationStrategy(Editor.Options)
            : new DefaultIndentationStrategy();

        OnCaretPositionChanged(this, EventArgs.Empty);
        UpdateCaretBrush();
        UpdateSearchHighlights();
        UpdateSquiggles();
        UpdateSemanticHighlights();
        CloseDiagnosticToolTip();
        _completionCts?.Cancel();
        _completionWindow?.Close();
        _insightWindow?.Close();
        ApplyPendingNavigation();
    }

    /// <summary>Ctrl+Space requests completion; Ctrl+Shift+Space requests signature help.</summary>
    private void OnTextAreaPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space)
        {
            return;
        }

        switch (Keyboard.Modifiers)
        {
            case ModifierKeys.Control:
                e.Handled = true;
                _ = RequestCompletionAsync();
                break;
            case ModifierKeys.Control | ModifierKeys.Shift:
                e.Handled = true;
                _ = RequestSignatureHelpAsync();
                break;
        }
    }

    /// <summary>
    /// Identifier characters and '.' trigger completion while no window is open;
    /// '(' and ',' trigger signature help.
    /// </summary>
    private void OnTextEntered(object sender, TextCompositionEventArgs e)
    {
        if (e.Text is not [var typed])
        {
            return;
        }

        if (typed is '(' or ',')
        {
            _ = RequestSignatureHelpAsync();
            return;
        }

        if (typed is ')')
        {
            _insightWindow?.Close();
            return;
        }

        if (_completionWindow is null && (char.IsLetter(typed) || typed is '.' or '_'))
        {
            _ = RequestCompletionAsync();
        }
    }

    private async Task RequestSignatureHelpAsync()
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        Core.Completion.SignatureHelpInfo? info;
        try
        {
            info = await viewModel.GetSignatureHelpAsync(Editor.CaretOffset);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!ReferenceEquals(ViewModel, viewModel))
        {
            return;
        }

        _insightWindow?.Close();
        if (info is null || info.Signatures.Count == 0)
        {
            return;
        }

        var window = new OverloadInsightWindow(Editor.TextArea)
        {
            Provider = new RoslynOverloadProvider(info),
        };
        window.SetResourceReference(BackgroundProperty, "Brush.Menu.Popup.Background");
        window.SetResourceReference(ForegroundProperty, "Brush.Window.Foreground");
        window.Closed += (_, _) => _insightWindow = null;
        _insightWindow = window;
        window.Show();
    }

    private async Task RequestCompletionAsync()
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        _completionCts?.Cancel();
        var cts = new CancellationTokenSource();
        _completionCts = cts;

        Core.Completion.CompletionResultInfo? result;
        try
        {
            result = await viewModel.GetCompletionsAsync(Editor.CaretOffset, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // The editor may have moved on while the request ran.
        if (cts.IsCancellationRequested
            || result is null
            || result.Items.Count == 0
            || _completionWindow is not null
            || !ReferenceEquals(ViewModel, viewModel)
            || Editor.CaretOffset < result.ReplacementStart)
        {
            return;
        }

        var window = new CompletionWindow(Editor.TextArea)
        {
            StartOffset = result.ReplacementStart,
            EndOffset = Math.Max(result.ReplacementStart + result.ReplacementLength, Editor.CaretOffset),
        };
        window.SetResourceReference(BackgroundProperty, "Brush.Menu.Popup.Background");
        window.CompletionList.ListBox.SetResourceReference(BackgroundProperty, "Brush.Menu.Popup.Background");
        window.CompletionList.ListBox.SetResourceReference(ForegroundProperty, "Brush.Window.Foreground");

        foreach (var item in result.Items)
        {
            window.CompletionList.CompletionData.Add(new LanguageCompletionData(item, viewModel));
        }

        window.Closed += (_, _) => _completionWindow = null;
        _completionWindow = window;
        window.Show();

        // Preselect against what is already typed (e.g. Ctrl+Space mid-word).
        var prefixLength = Editor.CaretOffset - window.StartOffset;
        if (prefixLength > 0)
        {
            window.CompletionList.SelectItem(Editor.Document.GetText(window.StartOffset, prefixLength));
        }
    }

    private void ObserveDocument(DocumentViewModel viewModel)
    {
        if (ReferenceEquals(_observedDocument, viewModel))
        {
            return;
        }

        if (_observedDocument is not null)
        {
            _observedDocument.PropertyChanged -= OnDocumentPropertyChanged;
        }

        _observedDocument = viewModel;
        viewModel.PropertyChanged += OnDocumentPropertyChanged;
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DocumentViewModel.PendingNavigation):
                ApplyPendingNavigation();
                break;
            case nameof(DocumentViewModel.SearchHighlights):
            case nameof(DocumentViewModel.CurrentSearchHighlight):
                UpdateSearchHighlights();
                break;
            case nameof(DocumentViewModel.Diagnostics):
                UpdateSquiggles();
                break;
            case nameof(DocumentViewModel.SemanticHighlights):
                UpdateSemanticHighlights();
                break;
        }
    }

    private void UpdateSemanticHighlights()
    {
        _semanticColorizer.Spans = ViewModel?.SemanticHighlights ?? [];
        Editor.TextArea.TextView.Redraw();
    }

    private void UpdateSquiggles()
    {
        _squiggleRenderer.Diagnostics = ViewModel?.Diagnostics ?? [];
        Editor.TextArea.TextView.Redraw();
    }

    /// <summary>
    /// Shows diagnostics and Roslyn Quick Info for the hovered position in one
    /// tooltip. Quick Info arrives asynchronously; a token guards against the
    /// mouse having moved on (or the tab having switched) in the meantime.
    /// </summary>
    private async void OnTextViewMouseHover(object? sender, MouseEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        var position = Editor.GetPositionFromPoint(e.GetPosition(Editor));
        if (position is not { } hoverPosition)
        {
            return;
        }

        var token = ++_hoverToken;

        var sections = new List<string>();
        if (viewModel.Diagnostics is { Count: > 0 } diagnostics)
        {
            sections.AddRange(diagnostics
                .Where(d => d.Line == hoverPosition.Line
                            && hoverPosition.Column >= d.Column
                            && hoverPosition.Column <= d.Column + Math.Max(d.Length, 1))
                .Select(d => d.Message));
        }

        try
        {
            var offset = Editor.Document.GetOffset(hoverPosition.Location);
            if (await viewModel.GetQuickInfoAsync(offset) is { } quickInfo)
            {
                sections.Insert(0, quickInfo);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ArgumentOutOfRangeException)
        {
            // Stale position or cancelled lookup; show whatever else we have.
        }

        if (token != _hoverToken || !ReferenceEquals(ViewModel, viewModel))
        {
            return;
        }

        CloseDiagnosticToolTip();
        if (sections.Count == 0)
        {
            return;
        }

        _diagnosticToolTip = new ToolTip
        {
            Content = new TextBlock
            {
                Text = string.Join(Environment.NewLine + Environment.NewLine, sections),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 600,
            },
            PlacementTarget = Editor,
            IsOpen = true,
        };
        e.Handled = true;
    }

    private void CloseDiagnosticToolTip()
    {
        if (_diagnosticToolTip is not null)
        {
            _diagnosticToolTip.IsOpen = false;
            _diagnosticToolTip = null;
        }
    }

    private void UpdateSearchHighlights()
    {
        _searchHighlightRenderer.Highlights = ViewModel?.SearchHighlights ?? [];
        _searchHighlightRenderer.CurrentHighlight = ViewModel?.CurrentSearchHighlight;
        Editor.TextArea.TextView.Redraw();
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.SelectedText = Editor.SelectedText;
        }
    }

    /// <summary>
    /// Consumes <see cref="DocumentViewModel.PendingNavigation"/> once the editor is
    /// bound to the requesting document, clamping to the current document bounds.
    /// </summary>
    private void ApplyPendingNavigation()
    {
        var viewModel = ViewModel;
        var navigation = viewModel?.PendingNavigation;
        if (viewModel is null || navigation is null || !ReferenceEquals(Editor.Document, viewModel.Document))
        {
            return;
        }

        viewModel.PendingNavigation = null;

        var line = Math.Clamp(navigation.Line, 1, Editor.Document.LineCount);
        var documentLine = Editor.Document.GetLineByNumber(line);
        var column = Math.Clamp(navigation.Column, 1, documentLine.Length + 1);
        var offset = documentLine.Offset + column - 1;
        var length = Math.Clamp(navigation.SelectionLength, 0, documentLine.EndOffset - offset);

        Editor.Select(offset, length);
        Editor.ScrollTo(line, column);
        if (navigation.FocusEditor)
        {
            Editor.TextArea.Focus();
        }
    }

    private void ObserveOptions(EditorOptionsViewModel options)
    {
        if (ReferenceEquals(_observedOptions, options))
        {
            return;
        }

        if (_observedOptions is not null)
        {
            _observedOptions.PropertyChanged -= OnOptionsPropertyChanged;
        }

        _observedOptions = options;
        options.PropertyChanged += OnOptionsPropertyChanged;
    }

    private void OnOptionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorOptionsViewModel.TabWidth) && _observedOptions is not null)
        {
            ApplyTabWidth(_observedOptions.TabWidth);
        }
    }

    private void ApplyTabWidth(int tabWidth) => Editor.Options.IndentationSize = Math.Max(1, tabWidth);

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        var caret = Editor.TextArea.Caret;
        ViewModel?.UpdateCaret(caret.Line, caret.Column);
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            var options = ViewModel?.Options;
            if (options is not null)
            {
                if (e.Delta > 0)
                {
                    options.ZoomInCommand.Execute(null);
                }
                else
                {
                    options.ZoomOutCommand.Execute(null);
                }

                e.Handled = true;
                return;
            }
        }

        base.OnPreviewMouseWheel(e);
    }
}
