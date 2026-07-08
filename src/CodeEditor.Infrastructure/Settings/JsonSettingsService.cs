using System.Text.Json;
using System.Text.Json.Serialization;
using CodeEditor.Application.Interfaces;
using CodeEditor.Application.Models;
using Microsoft.Extensions.Logging;

namespace CodeEditor.Infrastructure.Settings;

/// <summary>
/// Persists <see cref="EditorSettings"/> as JSON under %APPDATA%\CodeEditor\settings.json.
/// Writes are serialized and performed atomically (temp file + move) so a crash
/// mid-write cannot corrupt the settings file. The file is watched: external edits
/// are reloaded (debounced) and announced via <see cref="SettingsReloaded"/>; the
/// service's own saves are recognized by content comparison and raise nothing.
/// </summary>
public sealed class JsonSettingsService : ISettingsService, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly TimeSpan ReloadDebounce = TimeSpan.FromMilliseconds(400);

    private readonly ILogger<JsonSettingsService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Timer _reloadTimer;
    private FileSystemWatcher? _watcher;

    /// <param name="logger">Diagnostic logger.</param>
    /// <param name="settingsFilePath">
    /// Overrides the settings file location (used by tests); null for the
    /// standard %APPDATA%\CodeEditor\settings.json.
    /// </param>
    public JsonSettingsService(ILogger<JsonSettingsService> logger, string? settingsFilePath = null)
    {
        _logger = logger;

        SettingsFilePath = settingsFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CodeEditor",
            "settings.json");
        _reloadTimer = new Timer(_ => ReloadIfChangedAsync().GetAwaiter().GetResult());
    }

    public EditorSettings Settings { get; private set; } = new();

    public string SettingsFilePath { get; }

    public event EventHandler? SettingsReloaded;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var settings = await ReadFileAsync(cancellationToken).ConfigureAwait(false);
                if (settings is not null)
                {
                    Settings = settings;
                    _logger.LogInformation("Settings loaded from {Path}", SettingsFilePath);
                }
            }
            else
            {
                _logger.LogInformation("No settings file found at {Path}; using defaults", SettingsFilePath);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to load settings from {Path}; using defaults", SettingsFilePath);
            Settings = new EditorSettings();
        }

        StartWatching();
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath)!;
            Directory.CreateDirectory(directory);

            var tempFilePath = SettingsFilePath + ".tmp";
            var stream = File.Create(tempFilePath);
            // See ReadFileAsync: the implicit DisposeAsync must not capture the sync context.
            await using (stream.ConfigureAwait(false))
            {
                await JsonSerializer
                    .SerializeAsync(stream, Settings, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(tempFilePath, SettingsFilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to save settings to {Path}", SettingsFilePath);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _reloadTimer.Dispose();
        _writeLock.Dispose();
    }

    /// <summary>Watches the settings file for external edits. Called once, after the initial load.</summary>
    private void StartWatching()
    {
        if (_watcher is not null)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath)!;
            Directory.CreateDirectory(directory);

            _watcher = new FileSystemWatcher(directory, Path.GetFileName(SettingsFilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            // Editors save in many ways (in-place write, temp+rename, delete+create),
            // so all mutation events funnel into one debounced reload.
            _watcher.Changed += (_, _) => ScheduleReload();
            _watcher.Created += (_, _) => ScheduleReload();
            _watcher.Renamed += (_, _) => ScheduleReload();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Settings hot-reload unavailable: could not watch {Path}", SettingsFilePath);
        }
    }

    private void ScheduleReload() => _reloadTimer.Change(ReloadDebounce, Timeout.InfiniteTimeSpan);

    /// <summary>
    /// Debounced watcher callback: reloads the file and, when its content genuinely
    /// differs from the in-memory settings (i.e. the change was not our own save),
    /// swaps <see cref="Settings"/> and raises <see cref="SettingsReloaded"/>.
    /// </summary>
    private async Task ReloadIfChangedAsync()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return;
            }

            EditorSettings? reloaded;
            // Serialize against saves so a half-finished File.Move is never read.
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                reloaded = await ReadFileAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }

            if (reloaded is null)
            {
                return;
            }

            // Content comparison filters out our own saves and no-op edits.
            if (JsonSerializer.Serialize(reloaded, SerializerOptions)
                == JsonSerializer.Serialize(Settings, SerializerOptions))
            {
                return;
            }

            Settings = reloaded;
            _logger.LogInformation("Settings reloaded after external change to {Path}", SettingsFilePath);
            SettingsReloaded?.Invoke(this, EventArgs.Empty);
        }
        catch (ObjectDisposedException)
        {
            // Disposed while a reload was pending (shutdown) — nothing to do.
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Transient locks and half-written JSON are expected while an external
            // editor is saving; the next watcher event retries.
            _logger.LogWarning(ex, "Could not reload settings from {Path}; keeping current values", SettingsFilePath);
        }
    }

    private async Task<EditorSettings?> ReadFileAsync(CancellationToken cancellationToken)
    {
        var stream = File.OpenRead(SettingsFilePath);
        // ConfigureAwait(false) must also cover the implicit DisposeAsync, or a
        // caller blocking on this task from the UI thread would deadlock.
        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer
                .DeserializeAsync<EditorSettings>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
