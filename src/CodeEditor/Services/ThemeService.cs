using System.Windows;
using CodeEditor.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace CodeEditor.Services;

/// <summary>
/// Applies themes by swapping the theme <see cref="ResourceDictionary"/> in the
/// application's merged dictionaries. Additional themes can be registered at runtime
/// by pointing at any resource dictionary that defines the standard brush keys.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private sealed record ThemeDefinition(ThemeInfo Info, Uri ResourceUri);

    private readonly List<ThemeDefinition> _themes = [];
    private readonly ILogger<ThemeService> _logger;

    private ThemeDefinition _current;
    private ResourceDictionary? _currentDictionary;

    public ThemeService(ILogger<ThemeService> logger)
    {
        _logger = logger;

        // Ids stay "Dark"/"Light" so persisted settings keep working; the display
        // names carry the design-language identity (docs/DESIGN.md).
        _themes.Add(new ThemeDefinition(
            new ThemeInfo("Dark", "Ember Dark"),
            new Uri("pack://application:,,,/Themes/DarkTheme.xaml")));
        _themes.Add(new ThemeDefinition(
            new ThemeInfo("Light", "Ember Light"),
            new Uri("pack://application:,,,/Themes/LightTheme.xaml")));

        // App.xaml merges the dark theme by default; keep our state consistent with that.
        _current = _themes[0];
    }

    public event EventHandler<ThemeInfo>? ThemeChanged;

    public IReadOnlyList<ThemeInfo> AvailableThemes => [.. _themes.Select(theme => theme.Info)];

    public ThemeInfo CurrentTheme => _current.Info;

    public void ApplyTheme(string themeId)
    {
        var definition = _themes.FirstOrDefault(theme =>
            string.Equals(theme.Info.Id, themeId, StringComparison.OrdinalIgnoreCase));

        if (definition is null)
        {
            _logger.LogWarning("Unknown theme '{ThemeId}'; falling back to '{Fallback}'", themeId, _themes[0].Info.Id);
            definition = _themes[0];
        }

        var dictionary = new ResourceDictionary { Source = definition.ResourceUri };
        var merged = System.Windows.Application.Current.Resources.MergedDictionaries;

        // App.xaml declares the startup theme with a relative Source
        // ("Themes/DarkTheme.xaml"), so it must be resolved to an absolute pack URI
        // before comparing — otherwise the first ApplyTheme fails to find it,
        // inserts the new theme *before* it, and the startup theme keeps winning
        // (later merged dictionaries take precedence in WPF).
        var previous = _currentDictionary
            ?? merged.FirstOrDefault(existing =>
                existing.Source is { } source
                && _themes.Any(theme => theme.ResourceUri == ToAbsolutePackUri(source)));

        var index = previous is not null ? merged.IndexOf(previous) : -1;
        if (index >= 0)
        {
            merged[index] = dictionary;
        }
        else
        {
            merged.Insert(0, dictionary);
        }

        _currentDictionary = dictionary;
        _current = definition;
        _logger.LogInformation("Applied theme '{ThemeId}'", definition.Info.Id);
        ThemeChanged?.Invoke(this, definition.Info);
    }

    private static Uri ToAbsolutePackUri(Uri source)
        => source.IsAbsoluteUri ? source : new Uri(new Uri("pack://application:,,,/"), source);
}
