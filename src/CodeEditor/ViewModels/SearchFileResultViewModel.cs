using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CodeEditor.ViewModels;

/// <summary>
/// Groups the search matches of one file in the results tree.
/// </summary>
public sealed partial class SearchFileResultViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    public SearchFileResultViewModel(string filePath, string rootPath)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);

        var relativeDirectory = string.IsNullOrEmpty(rootPath)
            ? Path.GetDirectoryName(filePath)
            : Path.GetDirectoryName(Path.GetRelativePath(rootPath, filePath));
        RelativeDirectory = string.IsNullOrEmpty(relativeDirectory) || relativeDirectory == "."
            ? string.Empty
            : relativeDirectory;
    }

    public string FilePath { get; }

    public string FileName { get; }

    /// <summary>Directory of the file relative to the workspace root (empty at the root).</summary>
    public string RelativeDirectory { get; }

    public ObservableCollection<SearchMatchViewModel> Matches { get; } = [];
}
