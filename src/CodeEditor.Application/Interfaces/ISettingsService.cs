using CodeEditor.Application.Models;

namespace CodeEditor.Application.Interfaces;

/// <summary>
/// Loads and persists <see cref="EditorSettings"/>.
/// </summary>
public interface ISettingsService
{
    /// <summary>The current settings instance. Populated with defaults until <see cref="LoadAsync"/> completes.</summary>
    EditorSettings Settings { get; }

    /// <summary>Absolute path of the backing settings file.</summary>
    string SettingsFilePath { get; }

    /// <summary>Loads settings from disk, falling back to defaults if the file is missing or invalid.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the current settings to disk.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised after the settings file was modified externally (e.g. hand-edited) and
    /// <see cref="Settings"/> was replaced with the reloaded values. Not raised for the
    /// service's own saves. May be raised on a background thread — subscribers that
    /// touch UI state must marshal to the UI thread.
    /// </summary>
    event EventHandler? SettingsReloaded;
}
