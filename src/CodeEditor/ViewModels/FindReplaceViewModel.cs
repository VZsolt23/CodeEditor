using System.ComponentModel;
using CodeEditor.Core.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CodeEditor.ViewModels;

/// <summary>
/// Drives the inline find/replace panel over the active document: match
/// computation and highlighting, next/previous navigation, and replacement.
/// Matches recompute on query, option, document-text, and active-tab changes.
/// </summary>
public sealed partial class FindReplaceViewModel : ObservableObject
{
    private const int MaxMatches = 10_000;

    private readonly DocumentsViewModel _documents;

    private DocumentViewModel? _observedDocument;
    private IReadOnlyList<TextSpan> _matches = [];
    private int _currentIndex = -1;
    private bool _suppressRecompute;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private bool _isReplaceVisible;

    [ObservableProperty]
    private string _findText = string.Empty;

    [ObservableProperty]
    private string _replaceText = string.Empty;

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _wholeWord;

    /// <summary>"3 of 12", "No results", or a replace confirmation.</summary>
    [ObservableProperty]
    private string _matchStatus = string.Empty;

    public FindReplaceViewModel(DocumentsViewModel documents)
    {
        _documents = documents;
        documents.PropertyChanged += OnDocumentsPropertyChanged;
    }

    /// <summary>Raised when the panel wants keyboard focus in the find box.</summary>
    public event EventHandler? FocusFindRequested;

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private void OpenFind() => Open(showReplace: false);

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private void OpenReplace() => Open(showReplace: true);

    [RelayCommand]
    private void Close()
    {
        IsVisible = false;
        MatchStatus = string.Empty;
        SetMatches([], -1);
    }

    [RelayCommand(CanExecute = nameof(HasMatches))]
    private void FindNext() => MoveToMatch((_currentIndex + 1) % _matches.Count);

    [RelayCommand(CanExecute = nameof(HasMatches))]
    private void FindPrevious() => MoveToMatch((_currentIndex - 1 + _matches.Count) % _matches.Count);

    /// <summary>Replaces the current match and advances to the next one.</summary>
    [RelayCommand(CanExecute = nameof(HasMatches))]
    private void Replace()
    {
        if (_documents.ActiveDocument is not { } document || _currentIndex < 0)
        {
            return;
        }

        var match = _matches[_currentIndex];
        _suppressRecompute = true;
        try
        {
            document.Document.Replace(match.Start, match.Length, ReplaceText);
        }
        finally
        {
            _suppressRecompute = false;
        }

        Recompute(match.Start + ReplaceText.Length);
    }

    /// <summary>Replaces every match as a single undoable edit.</summary>
    [RelayCommand(CanExecute = nameof(HasMatches))]
    private void ReplaceAll()
    {
        if (_documents.ActiveDocument is not { } document || _matches.Count == 0)
        {
            return;
        }

        var replaced = _matches.Count;
        _suppressRecompute = true;
        try
        {
            using (document.Document.RunUpdate())
            {
                // Back to front so earlier offsets stay valid while replacing.
                for (var i = _matches.Count - 1; i >= 0; i--)
                {
                    document.Document.Replace(_matches[i].Start, _matches[i].Length, ReplaceText);
                }
            }
        }
        finally
        {
            _suppressRecompute = false;
        }

        Recompute(0, navigate: false);
        MatchStatus = replaced == 1 ? "Replaced 1 occurrence." : $"Replaced {replaced} occurrences.";
    }

    partial void OnFindTextChanged(string value) => Recompute(CaretOffset());

    partial void OnMatchCaseChanged(bool value) => Recompute(CaretOffset());

    partial void OnWholeWordChanged(bool value) => Recompute(CaretOffset());

    private bool HasActiveDocument() => _documents.ActiveDocument is not null;

    private bool HasMatches() => _matches.Count > 0;

    private void Open(bool showReplace)
    {
        if (_documents.ActiveDocument is not { } document)
        {
            return;
        }

        IsReplaceVisible = showReplace;
        IsVisible = true;

        // Seed the query from a single-line editor selection, VS Code style.
        var selection = document.SelectedText;
        if (selection.Length > 0 && selection.IndexOfAny(['\r', '\n']) < 0)
        {
            FindText = selection;
        }

        Recompute(CaretOffset());
        FocusFindRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Recomputes matches for the active document; the current match becomes the first
    /// one at or after <paramref name="anchorOffset"/> (wrapping to the start). Navigation
    /// is opt-in: editor-typing and tab-switch recomputes must not move the user's caret.
    /// </summary>
    private void Recompute(int anchorOffset, bool navigate = true)
    {
        if (_suppressRecompute)
        {
            return;
        }

        if (!IsVisible || _documents.ActiveDocument is not { } document || FindText.Length == 0)
        {
            SetMatches([], -1);
            MatchStatus = string.Empty;
            return;
        }

        var matches = TextSearcher.FindAll(document.Document.Text, FindText, MatchCase, WholeWord);
        if (matches.Count > MaxMatches)
        {
            matches.RemoveRange(MaxMatches, matches.Count - MaxMatches);
        }

        var index = -1;
        if (matches.Count > 0)
        {
            index = 0;
            for (var i = 0; i < matches.Count; i++)
            {
                if (matches[i].Start >= anchorOffset)
                {
                    index = i;
                    break;
                }
            }
        }

        SetMatches(matches, index);
        if (navigate && index >= 0)
        {
            NavigateToCurrent();
        }

        UpdateStatus();
    }

    private void MoveToMatch(int index)
    {
        _currentIndex = index;
        if (_documents.ActiveDocument is { } document)
        {
            document.CurrentSearchHighlight = _matches[index];
        }

        NavigateToCurrent();
        UpdateStatus();
    }

    private void NavigateToCurrent()
    {
        if (_documents.ActiveDocument is not { } document || _currentIndex < 0)
        {
            return;
        }

        var match = _matches[_currentIndex];
        if (match.End > document.Document.TextLength)
        {
            return;
        }

        var location = document.Document.GetLocation(match.Start);
        document.NavigateTo(location.Line, location.Column, match.Length, focusEditor: false);
    }

    private void SetMatches(IReadOnlyList<TextSpan> matches, int index)
    {
        _matches = matches;
        _currentIndex = index;

        if (_documents.ActiveDocument is { } document)
        {
            document.SearchHighlights = matches.Count > 0 ? matches : null;
            document.CurrentSearchHighlight = index >= 0 ? matches[index] : null;
        }

        FindNextCommand.NotifyCanExecuteChanged();
        FindPreviousCommand.NotifyCanExecuteChanged();
        ReplaceCommand.NotifyCanExecuteChanged();
        ReplaceAllCommand.NotifyCanExecuteChanged();
    }

    private void UpdateStatus()
        => MatchStatus = _matches.Count == 0
            ? "No results"
            : $"{_currentIndex + 1} of {_matches.Count}";

    private int CaretOffset()
    {
        if (_documents.ActiveDocument is not { } document)
        {
            return 0;
        }

        try
        {
            return document.Document.GetOffset(document.CaretLine, document.CaretColumn);
        }
        catch (ArgumentOutOfRangeException)
        {
            return 0;
        }
    }

    private void OnDocumentsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DocumentsViewModel.ActiveDocument))
        {
            return;
        }

        if (_observedDocument is not null)
        {
            _observedDocument.Document.Changed -= OnDocumentTextChanged;
            _observedDocument.SearchHighlights = null;
            _observedDocument.CurrentSearchHighlight = null;
        }

        _observedDocument = _documents.ActiveDocument;
        if (_observedDocument is not null)
        {
            _observedDocument.Document.Changed += OnDocumentTextChanged;
        }

        OpenFindCommand.NotifyCanExecuteChanged();
        OpenReplaceCommand.NotifyCanExecuteChanged();

        if (_observedDocument is null)
        {
            Close();
        }
        else if (IsVisible)
        {
            Recompute(0, navigate: false);
        }
    }

    private void OnDocumentTextChanged(object? sender, EventArgs e) => Recompute(CaretOffset(), navigate: false);
}
