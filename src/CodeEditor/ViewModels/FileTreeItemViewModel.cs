using System.Collections.ObjectModel;
using System.Windows.Media;
using CodeEditor.Core.Workspace;
using CodeEditor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CodeEditor.ViewModels;

/// <summary>
/// One node of the explorer tree. Directory children are loaded lazily on first
/// expansion; a placeholder child keeps the expander glyph visible until then.
/// </summary>
public sealed partial class FileTreeItemViewModel : ObservableObject
{
    private static readonly FileTreeItemViewModel LoadingPlaceholder = new();

    private readonly ExplorerViewModel _explorer;
    private bool _childrenLoaded;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconGlyph))]
    private bool _isExpanded;

    public FileTreeItemViewModel(FileSystemEntry entry, ExplorerViewModel explorer, FileTreeItemViewModel? parent)
    {
        Name = entry.Name;
        FullPath = entry.FullPath;
        IsDirectory = entry.IsDirectory;
        Parent = parent;
        _explorer = explorer;

        if (IsDirectory)
        {
            Children.Add(LoadingPlaceholder);
        }
        else if (FileIconCatalog.TryResolve(Name, out var languageGlyph, out var languageBrush))
        {
            LanguageIconGlyph = languageGlyph;
            LanguageIconBrush = languageBrush;
        }
    }

    /// <summary>Placeholder-only constructor; the instance is never interacted with.</summary>
    private FileTreeItemViewModel()
    {
        Name = string.Empty;
        FullPath = string.Empty;
        _explorer = null!;
    }

    public string Name { get; }

    public string FullPath { get; }

    public bool IsDirectory { get; }

    /// <summary>The containing directory node, or null for the workspace root.</summary>
    public FileTreeItemViewModel? Parent { get; }

    public bool IsRoot => Parent is null;

    public ObservableCollection<FileTreeItemViewModel> Children { get; } = [];

    /// <summary>Whether children have been enumerated (refreshes are skipped otherwise).</summary>
    public bool HasLoadedChildren => _childrenLoaded;

    /// <summary>Segoe MDL2 Assets glyph shown for folders and files without a language icon.</summary>
    public string IconGlyph => IsDirectory
        ? (IsExpanded ? "\uE838" : "\uE8B7")
        : "\uE7C3";

    /// <summary>Devicon glyph for this file's type, or empty when none applies.</summary>
    public string LanguageIconGlyph { get; } = string.Empty;

    /// <summary>Brand-color brush for <see cref="LanguageIconGlyph"/>, or null when none applies.</summary>
    public Brush? LanguageIconBrush { get; }

    /// <summary>Whether a Devicon language icon (rather than the generic glyph) should be shown.</summary>
    public bool UsesLanguageIcon => LanguageIconBrush is not null;

    /// <summary>
    /// Re-enumerates this directory's children, reusing existing child ViewModels
    /// (and therefore their expansion state) where paths still match.
    /// </summary>
    public void ReloadChildren()
    {
        if (!IsDirectory || !_childrenLoaded)
        {
            return;
        }

        var existing = Children
            .Where(child => !ReferenceEquals(child, LoadingPlaceholder))
            .ToDictionary(child => child.FullPath, StringComparer.OrdinalIgnoreCase);

        Children.Clear();
        foreach (var entry in _explorer.GetEntriesSafe(FullPath))
        {
            Children.Add(
                existing.TryGetValue(entry.FullPath, out var child) && child.IsDirectory == entry.IsDirectory
                    ? child
                    : new FileTreeItemViewModel(entry, _explorer, this));
        }
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_childrenLoaded)
        {
            _childrenLoaded = true;
            ReloadChildren();
        }
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (!IsDirectory)
        {
            await _explorer.OpenFileAsync(FullPath);
        }
    }

    [RelayCommand]
    private Task NewFileAsync() => _explorer.CreateEntryAsync(this, createFolder: false);

    [RelayCommand]
    private Task NewFolderAsync() => _explorer.CreateEntryAsync(this, createFolder: true);

    [RelayCommand]
    private Task RenameAsync() => _explorer.RenameAsync(this);

    [RelayCommand]
    private Task DeleteAsync() => _explorer.DeleteAsync(this);

    [RelayCommand]
    private void Refresh() => ReloadChildren();

    [RelayCommand]
    private void CopyPath() => _explorer.CopyPathToClipboard(FullPath);
}
