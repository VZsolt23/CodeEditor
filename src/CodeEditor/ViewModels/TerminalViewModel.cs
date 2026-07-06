using System.ComponentModel;
using System.IO;
using System.Text;
using CodeEditor.Application.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CodeEditor.ViewModels;

/// <summary>
/// Drives the Terminal panel: a lazily started shell session, its transcript
/// (capped), and the input line. Output chunks arrive on background threads in
/// bursts and are flushed to <see cref="Text"/> in one dispatcher pass.
/// </summary>
public sealed partial class TerminalViewModel : ObservableObject
{
    private const int MaxTranscriptLength = 200_000;

    private readonly ITerminalService _terminalService;
    private readonly IWorkspaceService _workspaceService;
    private readonly ILogger<TerminalViewModel> _logger;
    private readonly StringBuilder _transcript = new();
    private readonly Queue<string> _pendingChunks = new();
    private readonly object _pendingLock = new();

    private ITerminalSession? _session;
    private bool _flushScheduled;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    public TerminalViewModel(
        ITerminalService terminalService,
        IWorkspaceService workspaceService,
        ILogger<TerminalViewModel> logger)
    {
        _terminalService = terminalService;
        _workspaceService = workspaceService;
        _logger = logger;
    }

    /// <summary>Sends the input line to the shell, starting it on first use.</summary>
    [RelayCommand]
    private void Submit()
    {
        var command = InputText;
        InputText = string.Empty;

        EnsureSession();
        _session?.WriteLine(command);
    }

    /// <summary>Kills the current shell and starts a fresh one (in the current workspace root).</summary>
    [RelayCommand]
    private void Restart()
    {
        StopSession();
        EnsureSession();
    }

    [RelayCommand]
    private void ClearTranscript()
    {
        lock (_pendingLock)
        {
            _pendingChunks.Clear();
        }

        _transcript.Clear();
        Text = string.Empty;
    }

    private void EnsureSession()
    {
        if (_session is { IsRunning: true })
        {
            return;
        }

        StopSession();
        try
        {
            _session = _terminalService.Start(_workspaceService.RootPath);
            _session.OutputReceived += OnOutputReceived;
            _session.Exited += OnSessionExited;
            IsRunning = true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            _logger.LogError(ex, "Failed to start terminal shell");
            AppendChunk($"[Could not start the shell: {ex.Message}]\r\n");
            IsRunning = false;
        }
    }

    private void StopSession()
    {
        if (_session is null)
        {
            return;
        }

        _session.OutputReceived -= OnOutputReceived;
        _session.Exited -= OnSessionExited;
        _session.Dispose();
        _session = null;
        IsRunning = false;
    }

    private void OnOutputReceived(object? sender, string chunk)
    {
        lock (_pendingLock)
        {
            _pendingChunks.Enqueue(chunk);
            if (_flushScheduled)
            {
                return;
            }

            _flushScheduled = true;
        }

        RunOnDispatcher(FlushPendingChunks);
    }

    private void OnSessionExited(object? sender, int exitCode)
    {
        RunOnDispatcher(() =>
        {
            AppendChunk($"\r\n[Process exited with code {exitCode}. Press Enter or Restart to start a new shell.]\r\n");
            IsRunning = false;
        });
    }

    private void FlushPendingChunks()
    {
        string[] chunks;
        lock (_pendingLock)
        {
            chunks = [.. _pendingChunks];
            _pendingChunks.Clear();
            _flushScheduled = false;
        }

        foreach (var chunk in chunks)
        {
            _transcript.Append(chunk);
        }

        TrimAndPublish();
    }

    private void AppendChunk(string chunk)
    {
        _transcript.Append(chunk);
        TrimAndPublish();
    }

    private void TrimAndPublish()
    {
        if (_transcript.Length > MaxTranscriptLength)
        {
            _transcript.Remove(0, _transcript.Length - (MaxTranscriptLength * 3 / 4));
        }

        Text = _transcript.ToString();
    }

    private static void RunOnDispatcher(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
