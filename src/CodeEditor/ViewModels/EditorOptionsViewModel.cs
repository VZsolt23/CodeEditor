using CodeEditor.Application.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CodeEditor.ViewModels;

/// <summary>
/// Editor display options shared by all open editors (font, wrap, tabs, zoom).
/// Changes are written back to the settings model immediately; the settings file
/// itself is persisted on application exit. Zoom (font size) changes are transient.
/// </summary>
public sealed partial class EditorOptionsViewModel : ObservableObject
{
    private const double MinFontSize = 8;
    private const double MaxFontSize = 40;

    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private string _fontFamily;

    [ObservableProperty]
    private double _fontSize;

    [ObservableProperty]
    private bool _wordWrap;

    [ObservableProperty]
    private bool _showLineNumbers;

    [ObservableProperty]
    private int _tabWidth;

    public EditorOptionsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;

        var settings = settingsService.Settings;
        _fontFamily = settings.FontFamily;
        _fontSize = Math.Clamp(settings.FontSize, MinFontSize, MaxFontSize);
        _wordWrap = settings.WordWrap;
        _showLineNumbers = settings.ShowLineNumbers;
        _tabWidth = Math.Max(1, settings.TabWidth);
    }

    /// <summary>
    /// Re-reads the display options from the current settings (used by settings
    /// hot-reload). Also resets any transient zoom to the configured font size.
    /// </summary>
    public void ApplyFromSettings()
    {
        var settings = _settingsService.Settings;
        FontFamily = settings.FontFamily;
        FontSize = Math.Clamp(settings.FontSize, MinFontSize, MaxFontSize);
        WordWrap = settings.WordWrap;
        ShowLineNumbers = settings.ShowLineNumbers;
        TabWidth = Math.Max(1, settings.TabWidth);
    }

    partial void OnWordWrapChanged(bool value) => _settingsService.Settings.WordWrap = value;

    partial void OnShowLineNumbersChanged(bool value) => _settingsService.Settings.ShowLineNumbers = value;

    partial void OnTabWidthChanged(int value) => _settingsService.Settings.TabWidth = value;

    [RelayCommand]
    private void ZoomIn() => FontSize = Math.Min(MaxFontSize, FontSize + 1);

    [RelayCommand]
    private void ZoomOut() => FontSize = Math.Max(MinFontSize, FontSize - 1);

    [RelayCommand]
    private void ResetZoom() => FontSize = Math.Clamp(_settingsService.Settings.FontSize, MinFontSize, MaxFontSize);
}
