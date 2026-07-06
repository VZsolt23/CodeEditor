using CodeEditor.Application.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CodeEditor.ViewModels;

/// <summary>
/// A single selectable theme entry in the View → Theme menu.
/// </summary>
public sealed partial class ThemeOptionViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public ThemeOptionViewModel(ThemeInfo theme, IThemeService themeService)
    {
        Id = theme.Id;
        DisplayName = theme.DisplayName;
        SelectCommand = new RelayCommand(() => themeService.ApplyTheme(Id));
    }

    public string Id { get; }

    public string DisplayName { get; }

    public IRelayCommand SelectCommand { get; }
}
