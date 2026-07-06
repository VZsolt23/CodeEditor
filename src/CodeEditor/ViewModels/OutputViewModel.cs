using System.Collections.ObjectModel;
using System.Text;
using CodeEditor.Application.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CodeEditor.ViewModels;

/// <summary>
/// Drives the Output panel: the channel picker and the text of the selected
/// channel. Channel events can arrive on any thread and in bursts, so appended
/// lines are queued and flushed to <see cref="Text"/> in one dispatcher pass.
/// </summary>
public sealed partial class OutputViewModel : ObservableObject
{
    private const int RebuildThresholdLines = 6000;

    private readonly IOutputService _outputService;
    private readonly StringBuilder _buffer = new();
    private readonly Queue<string> _pendingLines = new();
    private readonly object _pendingLock = new();

    private IOutputChannel? _subscribedChannel;
    private bool _flushScheduled;
    private int _bufferedLineCount;

    [ObservableProperty]
    private IOutputChannel? _selectedChannel;

    [ObservableProperty]
    private string _text = string.Empty;

    public OutputViewModel(IOutputService outputService)
    {
        _outputService = outputService;

        foreach (var channel in outputService.Channels)
        {
            Channels.Add(channel);
        }

        outputService.ChannelAdded += OnChannelAdded;
        SelectedChannel = Channels.FirstOrDefault();
    }

    /// <summary>Available output channels, in creation order.</summary>
    public ObservableCollection<IOutputChannel> Channels { get; } = [];

    [RelayCommand]
    private void Clear() => SelectedChannel?.Clear();

    partial void OnSelectedChannelChanged(IOutputChannel? value)
    {
        if (_subscribedChannel is not null)
        {
            _subscribedChannel.LineAppended -= OnLineAppended;
            _subscribedChannel.Cleared -= OnChannelCleared;
        }

        _subscribedChannel = value;
        if (value is not null)
        {
            value.LineAppended += OnLineAppended;
            value.Cleared += OnChannelCleared;
        }

        RebuildFromSnapshot();
    }

    private void OnChannelAdded(object? sender, IOutputChannel channel)
    {
        RunOnDispatcher(() =>
        {
            Channels.Add(channel);
            SelectedChannel ??= channel;
        });
    }

    private void OnChannelCleared(object? sender, EventArgs e) => RunOnDispatcher(RebuildFromSnapshot);

    private void OnLineAppended(object? sender, string line)
    {
        lock (_pendingLock)
        {
            _pendingLines.Enqueue(line);
            if (_flushScheduled)
            {
                return;
            }

            _flushScheduled = true;
        }

        RunOnDispatcher(FlushPendingLines);
    }

    private void FlushPendingLines()
    {
        string[] lines;
        lock (_pendingLock)
        {
            lines = [.. _pendingLines];
            _pendingLines.Clear();
            _flushScheduled = false;
        }

        foreach (var line in lines)
        {
            _buffer.AppendLine(line);
        }

        _bufferedLineCount += lines.Length;
        if (_bufferedLineCount > RebuildThresholdLines)
        {
            // The channel trims its backlog; resync so the panel stays bounded too.
            RebuildFromSnapshot();
            return;
        }

        Text = _buffer.ToString();
    }

    private void RebuildFromSnapshot()
    {
        _buffer.Clear();
        _bufferedLineCount = 0;

        if (_subscribedChannel is not null)
        {
            var snapshot = _subscribedChannel.GetSnapshot();
            foreach (var line in snapshot)
            {
                _buffer.AppendLine(line);
            }

            _bufferedLineCount = snapshot.Count;
        }

        Text = _buffer.ToString();
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
