using System.Collections.ObjectModel;
using CodeEditor.Core.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CodeEditor.ViewModels;

/// <summary>
/// Drives the Problems panel: the diagnostics list and navigation to the
/// affected location. Language services (Phases 4–5) publish entries via
/// <see cref="SetDiagnostics"/>, replacing their previous set per source.
/// </summary>
public sealed partial class ProblemsViewModel : ObservableObject
{
    private readonly DocumentsViewModel _documents;

    public ProblemsViewModel(DocumentsViewModel documents)
    {
        _documents = documents;
        Diagnostics.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDiagnostics));
    }

    /// <summary>Current diagnostics, most severe first, then by file and location.</summary>
    public ObservableCollection<DiagnosticItem> Diagnostics { get; } = [];

    public bool HasDiagnostics => Diagnostics.Count > 0;

    /// <summary>
    /// Replaces all diagnostics published by <paramref name="source"/> with
    /// <paramref name="diagnostics"/> and re-sorts the list.
    /// </summary>
    public void SetDiagnostics(string source, IReadOnlyList<DiagnosticItem> diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var merged = Diagnostics
            .Where(item => !string.Equals(item.Source, source, StringComparison.OrdinalIgnoreCase))
            .Concat(diagnostics)
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Line)
            .ThenBy(item => item.Column)
            .ToList();

        Diagnostics.Clear();
        foreach (var item in merged)
        {
            Diagnostics.Add(item);
        }
    }

    /// <summary>Opens the diagnostic's file and moves the caret to its location.</summary>
    [RelayCommand]
    private async Task OpenDiagnosticAsync(DiagnosticItem? item)
    {
        if (item?.FilePath is not { } filePath)
        {
            return;
        }

        await _documents.OpenAsync(filePath);

        var document = _documents.ActiveDocument;
        if (document?.FilePath is not null
            && string.Equals(document.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            document.NavigateTo(Math.Max(1, item.Line), Math.Max(1, item.Column), selectionLength: 0);
        }
    }
}
