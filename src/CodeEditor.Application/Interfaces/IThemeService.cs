namespace CodeEditor.Application.Interfaces;

/// <summary>Identifies a UI theme available to the application.</summary>
/// <param name="Id">Stable identifier persisted in settings (e.g. "Dark").</param>
/// <param name="DisplayName">Human-readable name shown in menus.</param>
public sealed record ThemeInfo(string Id, string DisplayName);

/// <summary>
/// Manages the application's visual themes. Implementations own how theme
/// resources are applied to the UI framework; new themes can be registered at runtime.
/// </summary>
public interface IThemeService
{
    /// <summary>All registered themes.</summary>
    IReadOnlyList<ThemeInfo> AvailableThemes { get; }

    /// <summary>The currently applied theme.</summary>
    ThemeInfo CurrentTheme { get; }

    /// <summary>Applies the theme with the given id. Unknown ids fall back to the default theme.</summary>
    void ApplyTheme(string themeId);

    /// <summary>Raised after a theme has been applied.</summary>
    event EventHandler<ThemeInfo>? ThemeChanged;
}
