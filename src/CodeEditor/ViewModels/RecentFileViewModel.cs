using CommunityToolkit.Mvvm.Input;

namespace CodeEditor.ViewModels;

/// <summary>
/// A single entry in the File → Open Recent menu.
/// </summary>
public sealed class RecentFileViewModel(string path, Func<string, Task> openRecent)
{
    /// <summary>Full path of the recent file (shown as the menu header).</summary>
    public string Path { get; } = path;

    /// <summary>Opens the file, or removes it from the list if it no longer exists.</summary>
    public IAsyncRelayCommand OpenCommand { get; } = new AsyncRelayCommand(() => openRecent(path));
}
