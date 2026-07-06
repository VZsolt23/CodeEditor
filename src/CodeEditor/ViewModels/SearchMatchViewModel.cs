using CodeEditor.Core.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CodeEditor.ViewModels;

/// <summary>
/// One match line in the search results tree. Splits the display excerpt into
/// prefix / match / suffix so the view can highlight the matched text.
/// </summary>
public sealed partial class SearchMatchViewModel : ObservableObject
{
    private readonly SearchViewModel _owner;
    private readonly SearchMatch _match;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isExpanded;

    public SearchMatchViewModel(SearchMatch match, SearchViewModel owner)
    {
        _match = match;
        _owner = owner;

        LinePrefix = match.LineText[..match.LineTextMatchStart].TrimStart();
        MatchText = match.LineText.Substring(match.LineTextMatchStart, match.LineTextMatchLength);
        LineSuffix = match.LineText[(match.LineTextMatchStart + match.LineTextMatchLength)..].TrimEnd();
    }

    public string FilePath => _match.FilePath;

    public int LineNumber => _match.LineNumber;

    public int Column => _match.Column;

    public int MatchLength => _match.MatchLength;

    /// <summary>Line number prefix shown before the excerpt, e.g. "12:&#160;".</summary>
    public string LineNumberDisplay => $"{_match.LineNumber}: ";

    public string LinePrefix { get; }

    public string MatchText { get; }

    public string LineSuffix { get; }

    /// <summary>Opens the file and moves the caret to this match.</summary>
    [RelayCommand]
    private Task OpenAsync() => _owner.OpenMatchAsync(this);
}
