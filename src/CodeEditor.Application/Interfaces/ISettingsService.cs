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
}
