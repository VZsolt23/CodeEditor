using CodeEditor.Application.Interfaces;

namespace CodeEditor.Application.Services;

/// <summary>
/// Maintains the recent files list inside <see cref="ISettingsService.Settings"/> and
/// persists it through the settings service.
/// </summary>
public sealed class RecentFilesService(ISettingsService settingsService) : IRecentFilesService
{
    private readonly ISettingsService _settingsService = settingsService;

    public event EventHandler? RecentFilesChanged;

    public IReadOnlyList<string> RecentFiles => [.. _settingsService.Settings.RecentFiles];

    public async Task AddAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);
        var settings = _settingsService.Settings;

        settings.RecentFiles.RemoveAll(existing =>
            string.Equals(existing, fullPath, StringComparison.OrdinalIgnoreCase));
        settings.RecentFiles.Insert(0, fullPath);

        var limit = Math.Max(1, settings.RecentFilesLimit);
        if (settings.RecentFiles.Count > limit)
        {
            settings.RecentFiles.RemoveRange(limit, settings.RecentFiles.Count - limit);
        }

        // Notify before persisting: the list is already updated, and the event must
        // fire on the caller's thread (UI). Raising it after SaveAsync's
        // ConfigureAwait(false) would resume on a thread-pool thread and mutating a
        // bound ObservableCollection there throws a cross-thread CollectionView error.
        RecentFilesChanged?.Invoke(this, EventArgs.Empty);
        await _settingsService.SaveAsync().ConfigureAwait(false);
    }

    public async Task RemoveAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var removed = _settingsService.Settings.RecentFiles.RemoveAll(existing =>
            string.Equals(existing, filePath, StringComparison.OrdinalIgnoreCase));

        if (removed > 0)
        {
            // See AddAsync: notify on the caller's thread, then persist.
            RecentFilesChanged?.Invoke(this, EventArgs.Empty);
            await _settingsService.SaveAsync().ConfigureAwait(false);
        }
    }
}
