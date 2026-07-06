using System.Text.Json;
using System.Text.Json.Serialization;
using CodeEditor.Application.Interfaces;
using CodeEditor.Application.Models;
using Microsoft.Extensions.Logging;

namespace CodeEditor.Infrastructure.Settings;

/// <summary>
/// Persists <see cref="EditorSettings"/> as JSON under %APPDATA%\CodeEditor\settings.json.
/// Writes are serialized and performed atomically (temp file + move) so a crash
/// mid-write cannot corrupt the settings file.
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

    private readonly ILogger<JsonSettingsService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public JsonSettingsService(ILogger<JsonSettingsService> logger)
    {
        _logger = logger;

        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CodeEditor");
        SettingsFilePath = Path.Combine(settingsDirectory, "settings.json");
    }

    public EditorSettings Settings { get; private set; } = new();

    public string SettingsFilePath { get; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                _logger.LogInformation("No settings file found at {Path}; using defaults", SettingsFilePath);
                return;
            }

            EditorSettings? settings;
            var stream = File.OpenRead(SettingsFilePath);
            // ConfigureAwait(false) must also cover the implicit DisposeAsync, or a
            // caller blocking on this task from the UI thread would deadlock.
            await using (stream.ConfigureAwait(false))
            {
                settings = await JsonSerializer
                    .DeserializeAsync<EditorSettings>(stream, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (settings is not null)
            {
                Settings = settings;
                _logger.LogInformation("Settings loaded from {Path}", SettingsFilePath);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to load settings from {Path}; using defaults", SettingsFilePath);
            Settings = new EditorSettings();
        }
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
            // See LoadAsync: the implicit DisposeAsync must not capture the sync context.
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

    public void Dispose() => _writeLock.Dispose();
}
