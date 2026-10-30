using System.Collections.ObjectModel;
using System.Windows.Shell;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SSnip.App.Services;
using SSnip.Core.Downloads;
using SSnip.Core.Queue;
using SSnip.Core.Settings;

namespace SSnip.App.ViewModels;

public sealed record WhenFinishedOption(WhenQueueFinishes Action, string Name);

/// <summary>
/// The Queue screen, the status bar summary and taskbar progress. Queue events arrive on worker
/// threads and are applied on the UI thread.
/// </summary>
public sealed partial class QueueViewModel : ObservableObject
{
    private const int CountdownSeconds = 60;

    private readonly JobQueue _queue;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly IPowerService _power;
    private readonly ILogger<QueueViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<long, JobViewModel> _byId = [];
    private DispatcherTimer? _countdown;
    private int _secondsLeft;

    public QueueViewModel(JobQueue queue, ISettingsStore settings, IDialogService dialogs, INotificationService notifications, IPowerService power, ILogger<QueueViewModel> logger)
    {
        _queue = queue;
        _settings = settings;
        _dialogs = dialogs;
        _notifications = notifications;
        _power = power;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;
        SelectedWhenFinished = WhenFinishedOptions[0];
        Concurrency = settings.Current.MaxConcurrentDownloads;

        queue.JobChanged += (_, job) => _dispatcher.BeginInvoke(() => Upsert(job));
        queue.JobRemoved += (_, id) => _dispatcher.BeginInvoke(() => RemoveRow(id));
        queue.Finished += (_, _) => _dispatcher.BeginInvoke(OnQueueFinished);
        queue.StoreFailed += (_, ex) => _logger.LogError(ex, "Could not save the queue");
    }

    public ObservableCollection<JobViewModel> Jobs { get; } = [];

    public IReadOnlyList<int> ConcurrencyChoices { get; } = [.. Enumerable.Range(JobScheduler.MinConcurrency, JobScheduler.MaxConcurrency)];

    public IReadOnlyList<WhenFinishedOption> WhenFinishedOptions { get; } =
    [
        new(WhenQueueFinishes.DoNothing, "Do nothing"),
        new(WhenQueueFinishes.Sleep, "Sleep"),
        new(WhenQueueFinishes.ShutDown, "Shut down"),
    ];

    [ObservableProperty]
    public partial int Concurrency { get; set; }

    [ObservableProperty]
    public partial WhenFinishedOption SelectedWhenFinished { get; set; }

    [ObservableProperty]
    public partial string Summary { get; private set; } = "0 running, 0 queued";

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; } = true;

    [ObservableProperty]
    public partial TaskbarItemProgressState TaskbarState { get; private set; }

    [ObservableProperty]
    public partial double TaskbarValue { get; private set; }

    [ObservableProperty]
    public partial bool IsCountingDown { get; private set; }

    [ObservableProperty]
    public partial string CountdownText { get; private set; } = "";

    /// <summary>Call once before the queue loads, so the stored concurrency applies from the first job.</summary>
    public void Initialize()
    {
        _queue.SetConcurrency(Concurrency);
        foreach (var job in _queue.Jobs)
        {
            Upsert(job);
        }
    }

    public JobViewModel? Find(long id) => _byId.GetValueOrDefault(id);

    /// <summary>Adds or updates a row. Call on the UI thread.</summary>
    public JobViewModel Upsert(QueueJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!_byId.TryGetValue(job.Id, out var row))
        {
            // A late update for a job that was removed in the meantime.
            if (_queue.Find(job.Id) is null)
            {
                return new JobViewModel(job);
            }

            row = new JobViewModel(job);
            _byId[job.Id] = row;
            Jobs.Insert(IndexFor(job), row);
        }
        else
        {
            var wasActive = row.IsActive;
            var oldPosition = row.Job.Position;
            if (!row.Update(job))
            {
                return row;
            }

            if (job.Position != oldPosition)
            {
                Jobs.Remove(row);
                Jobs.Insert(IndexFor(job), row);
            }

            if (wasActive && job.State is JobState.Done or JobState.Failed)
            {
                NotifyFinished(job);
            }
        }

        RefreshSummary();
        return row;
    }

    partial void OnConcurrencyChanged(int value)
    {
        _queue.SetConcurrency(value);
        if (_settings.Current.MaxConcurrentDownloads != value)
        {
            _ = _settings.SaveAsync(_settings.Current with { MaxConcurrentDownloads = value });
        }
    }

    partial void OnSelectedWhenFinishedChanged(WhenFinishedOption value)
    {
        if (value.Action == WhenQueueFinishes.DoNothing)
        {
            CancelCountdown();
        }
    }

    [RelayCommand]
    private void Pause(JobViewModel job) => _queue.Pause(job.Id);

    [RelayCommand]
    private void Resume(JobViewModel job) => _queue.Resume(job.Id);

    [RelayCommand]
    private void Cancel(JobViewModel job) => _queue.Cancel(job.Id);

    [RelayCommand]
    private void Retry(JobViewModel job) => _queue.Retry(job.Id);

    [RelayCommand]
    private void StopRecording(JobViewModel job) => _queue.StopRecording(job.Id);

    [RelayCommand]
    private void MoveUp(JobViewModel job) => _queue.Move(job.Id, -1);

    [RelayCommand]
    private void MoveDown(JobViewModel job) => _queue.Move(job.Id, 1);

    [RelayCommand]
    private void Remove(JobViewModel job) => _queue.Remove(job.Id);

    [RelayCommand]
    private void OpenFolder(JobViewModel job)
    {
        if (job.Job.FilePath is { } path)
        {
            _dialogs.RevealInExplorer(path);
        }
        else
        {
            _dialogs.OpenInExplorer(job.Job.Options.OutputDirectory);
        }
    }

    [RelayCommand]
    private void OpenLog(JobViewModel job)
    {
        if (job.Job.LogPath is { } path)
        {
            _dialogs.OpenFile(path);
        }
    }

    [RelayCommand]
    private void CopyCommand(JobViewModel job)
    {
        if (job.Job.CommandLine is { } command)
        {
            _dialogs.SetClipboardText(command);
        }
    }

    [RelayCommand]
    private void PauseAll() => _queue.PauseAll();

    [RelayCommand]
    private void ResumeAll() => _queue.ResumeAll();

    [RelayCommand]
    private async Task ClearFinishedAsync()
    {
        var count = Jobs.Count(j => j.Job.IsFinished);
        if (count == 0)
        {
            return;
        }

        if (await _dialogs.ConfirmAsync("Clear finished jobs", $"Remove {count} done, failed and cancelled jobs from the queue? Downloaded files stay on disk.", "Clear"))
        {
            _queue.ClearFinished();
        }
    }

    [RelayCommand]
    private void CancelCountdown()
    {
        _countdown?.Stop();
        _countdown = null;
        IsCountingDown = false;
        CountdownText = "";
    }

    private void OnQueueFinished()
    {
        var summary = _queue.Summary;
        var action = SelectedWhenFinished.Action;
        var then = action switch
        {
            WhenQueueFinishes.Sleep => $" Sleeping in {CountdownSeconds} seconds.",
            WhenQueueFinishes.ShutDown => $" Shutting down in {CountdownSeconds} seconds.",
            _ => "",
        };
        _notifications.Show("Queue finished", $"{summary.Done} done, {summary.Failed} failed.{then}");

        if (action != WhenQueueFinishes.DoNothing)
        {
            StartCountdown(action);
        }
    }

    private void StartCountdown(WhenQueueFinishes action)
    {
        CancelCountdown();
        _secondsLeft = CountdownSeconds;
        IsCountingDown = true;
        UpdateCountdownText(action);

        _countdown = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += (_, _) =>
        {
            _secondsLeft--;
            if (_secondsLeft > 0)
            {
                UpdateCountdownText(action);
                return;
            }

            CancelCountdown();

            // One run only: after waking up, the next queue does not put the PC to sleep again.
            SelectedWhenFinished = WhenFinishedOptions[0];
            try
            {
                if (action == WhenQueueFinishes.Sleep)
                {
                    _power.Sleep();
                }
                else
                {
                    _power.ShutDown();
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                _logger.LogError(ex, "Could not {Action} the PC", action);
                _notifications.Show("Could not " + (action == WhenQueueFinishes.Sleep ? "sleep" : "shut down"), ex.Message);
            }
        };
        _countdown.Start();
    }

    private void UpdateCountdownText(WhenQueueFinishes action) =>
        CountdownText = $"The queue is finished. {(action == WhenQueueFinishes.Sleep ? "Sleeping" : "Shutting down")} in {_secondsLeft} s.";

    private void NotifyFinished(QueueJob job)
    {
        if (job.State == JobState.Failed)
        {
            _notifications.Show("Download failed", $"{job.Title}\n{job.Error}");
        }
        else
        {
            _notifications.Show(job.IsLive ? "Recording saved" : "Download finished", job.Title);
        }
    }

    private void RemoveRow(long id)
    {
        if (_byId.Remove(id, out var row))
        {
            Jobs.Remove(row);
            RefreshSummary();
        }
    }

    private int IndexFor(QueueJob job)
    {
        var index = 0;
        while (index < Jobs.Count && (Jobs[index].Job.Position, Jobs[index].Id).CompareTo((job.Position, job.Id)) < 0)
        {
            index++;
        }

        return index;
    }

    private void RefreshSummary()
    {
        var summary = _queue.Summary;
        Summary = summary.ToString();
        IsEmpty = Jobs.Count == 0;

        TaskbarValue = summary.Fraction ?? 0;
        TaskbarState = summary.Fraction is null
            ? TaskbarItemProgressState.None
            : summary.Running == 0 ? TaskbarItemProgressState.Paused
            : TaskbarItemProgressState.Normal;
    }
}
