namespace DellGamingControlCenter.App.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;

public enum GameReadinessState 
{
    Unknown,
    Ready,
    ProfileIncomplete,
    ExecutableMissing,
    UnsupportedConfiguration,
    NoHistory
}

public class ProfileCard : BaseViewModel
{
    private GameProfile _profile = null!;
    public GameProfile Profile 
    { 
        get => _profile; 
        set 
        {
            _profile = value; 
            _art = null; // artwork follows the (possibly new) game
            OnPropertyChanged(nameof(GameName));
            OnPropertyChanged(nameof(ExecutablePath));
            OnPropertyChanged(nameof(GpuPreferenceText));
            OnPropertyChanged(nameof(PowerModeText));
            OnPropertyChanged(nameof(PriorityText));
            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(Art));
            UpdateReadiness();
        }
    }

    private System.Windows.Media.ImageSource? _art;
    /// <summary>
    /// Offline artwork for this game (bundled art → exe icon → generated placeholder).
    /// Lazy + cached by <see cref="DellGamingControlCenter.App.Services.GameArtService"/>;
    /// null only if rendering is entirely unavailable (view hides the art area then).
    /// </summary>
    public System.Windows.Media.ImageSource? Art
    {
        get => _art ??= DellGamingControlCenter.App.Services.GameArtService.GetArt(GameName, ExecutablePath);
        set => SetProperty(ref _art, value);
    }

    public string GameName => Profile.GameName;
    public string ExecutablePath => Profile.ExecutablePath;
    public string GpuPreferenceText => Profile.PreferredGpu.ToString();
    public string PowerModeText => Profile.PerformanceMode.ToString();
    public string PriorityText => Profile.ProcessPriority.ToString();
    public bool IsEnabled => Profile.MonitoringEnabled;

    private GameReadinessState _readinessState = GameReadinessState.Unknown;
    public GameReadinessState ReadinessState { get => _readinessState; set => SetProperty(ref _readinessState, value); }

    private string _profileStatus = "Configured ✓";
    public string ProfileStatus { get => _profileStatus; set => SetProperty(ref _profileStatus, value); }

    public string LastPlayed { get; set; } = "Never";
    public int SessionCount { get; set; } = 0;
    public string TypicalDuration { get; set; } = "--";
    public string AverageCpu { get; set; } = "--";
    public string AverageRam { get; set; } = "--";

    public ObservableCollection<GamingInsight> GameInsights { get; } = new();

    public void UpdateReadiness()
    {
        if (string.IsNullOrWhiteSpace(ExecutablePath))
        {
            ReadinessState = GameReadinessState.ProfileIncomplete;
            ProfileStatus = "PROFILE INCOMPLETE";
            return;
        }

        if (!System.IO.File.Exists(ExecutablePath))
        {
            ReadinessState = GameReadinessState.ExecutableMissing;
            ProfileStatus = "EXECUTABLE MISSING";
            return;
        }

        bool gpuSupported = App.GpuPreferenceController.Status == CapabilityStatus.Supported;
        if (!gpuSupported && Profile.PreferredGpu != GpuPreference.Auto)
        {
            ReadinessState = GameReadinessState.UnsupportedConfiguration;
            ProfileStatus = "UNSUPPORTED CONFIGURATION";
            return;
        }
        
        ReadinessState = GameReadinessState.Ready;
        ProfileStatus = "READY";
    }
}

public class SessionCard
{
    public GameSession Session { get; }
    
    public SessionCard(GameSession session)
    {
        Session = session;
    }
    
    public string GameName => Session.GameName;
    public DateTime StartTime => Session.StartTime;
    public string Duration 
    {
        get 
        {
            if (Session.EndTime.HasValue)
            {
                var diff = Session.EndTime.Value - Session.StartTime;
                if (diff.TotalHours >= 1)
                    return $"{(int)diff.TotalHours}h {diff.Minutes}m";
                return $"{diff.Minutes}m {diff.Seconds}s";
            }
            return "In progress";
        }
    }
    
    public double AverageCpuUsage => Session.AverageCpuUsage;
    public double PeakCpuUsage => Session.PeakCpuUsage;
    public string AverageRamUsage => $"{Session.AverageRamUsageBytes / (1024.0 * 1024 * 1024):F1} GB";
    public string PeakRamUsage => $"{Session.PeakRamUsageBytes / (1024.0 * 1024 * 1024):F1} GB";
    
    public string GameStorageLocation => Session.GameStorageLocation;
    public List<DiagnosticFinding> SessionFindings => Session.SessionFindings;
}

public class RecentGameCard
{
    public string GameName { get; init; } = "";
    public string LastPlayedText { get; init; } = "";
    public int SessionCount { get; init; }
    public string StorageLocation { get; init; } = "";
    public string AverageCpu { get; init; } = "";
    public string AverageRam { get; init; } = "";
}

public class GameCenterViewModel : BaseViewModel
{
    private ObservableCollection<ProfileCard> _allProfiles = new();
    private ObservableCollection<ProfileCard> _filteredProfiles = new();
    private ObservableCollection<SessionCard> _recentSessions = new();
    private ObservableCollection<RecentGameCard> _recentGames = new();
    private string _searchText = "";
    
    // Active Session State
    private GameSession? _activeSession;
    private string _sessionDuration = "00:00:00";
    private bool _hasActiveSession;
    private DispatcherTimer _timer = null!;

    public ObservableCollection<ProfileCard> FilteredProfiles
    {
        get => _filteredProfiles;
        set => SetProperty(ref _filteredProfiles, value);
    }
    
    public ObservableCollection<SessionCard> RecentSessions
    {
        get => _recentSessions;
        set => SetProperty(ref _recentSessions, value);
    }

    public ObservableCollection<RecentGameCard> RecentGames
    {
        get => _recentGames;
        set => SetProperty(ref _recentGames, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public GameSession? ActiveSession 
    { 
        get => _activeSession; 
        private set 
        { 
            if (SetProperty(ref _activeSession, value))
            {
                HasActiveSession = _activeSession != null;
                OnPropertyChanged(nameof(ActiveGameName));
                OnPropertyChanged(nameof(ActiveProfileMode));
                OnPropertyChanged(nameof(AppliedPolicies));
                OnPropertyChanged(nameof(ActiveSessionDiagnostics));
            }
        }
    }

    public bool HasActiveSession
    {
        get => _hasActiveSession;
        set => SetProperty(ref _hasActiveSession, value);
    }

    public string ActiveGameName => ActiveSession?.GameName ?? "";
    public string ActiveExecutable => ActiveSession?.ExecutablePath ?? "";
    public string SessionDuration { get => _sessionDuration; set => SetProperty(ref _sessionDuration, value); }
    public string ActiveProfileMode => ActiveSession != null ? App.ConfigService.GetGameProfiles().FirstOrDefault(p => p.GameId == ActiveSession.GameId)?.PerformanceMode.ToString() ?? "Default" : "";
    public string ActiveGpuPreference => ActiveSession != null ? App.ConfigService.GetGameProfiles().FirstOrDefault(p => p.GameId == ActiveSession.GameId)?.PreferredGpu.ToString() ?? "Auto" : "";
    public string ActiveProcessPriority => ActiveSession != null ? App.ConfigService.GetGameProfiles().FirstOrDefault(p => p.GameId == ActiveSession.GameId)?.ProcessPriority.ToString() ?? "Normal" : "";
    
    public string AppliedPolicies => ActiveSession == null ? "" : string.Join("\n", ActiveSession.AppliedChanges.Select(a => $"• {a.Description}: {a.Value}"));
    
    public string ActiveSessionState 
    {
        get 
        {
            if (ActiveSession == null) return "No Active Game";
            return ActiveSession.CurrentState switch 
            {
                GameSessionState.Created => "Preparing",
                GameSessionState.Preparing => "Preparing",
                GameSessionState.Optimizing => "Preparing",
                GameSessionState.Launching => "Launching",
                GameSessionState.WaitingForProcess => "Starting",
                GameSessionState.Running => "Active",
                GameSessionState.Stopping => "Ending",
                GameSessionState.Restoring => "Ending",
                GameSessionState.Completed => "Completed",
                GameSessionState.CompletedWithWarnings => "Completed",
                GameSessionState.Failed => "Failed",
                GameSessionState.Cancelled => "Failed",
                _ => ActiveSession.CurrentState.ToString()
            };
        }
    }

    // B9: previous getters copied the entire 300-snapshot history buffer (twice per getter)
    // on every property re-evaluation. A single snapshot cache avoids the allocation churn
    // on this 2C/4T CPU.
    private DiagnosticSnapshot? _lastSnapshot;

    private void OnSnapshotCaptured(object? sender, DiagnosticSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        if (ActiveSession != null)
        {
            RunOnUIThread(() =>
            {
                OnPropertyChanged(nameof(ActiveCpuUsage));
                OnPropertyChanged(nameof(ActiveRamUsage));
                OnPropertyChanged(nameof(ActiveGpuUsage));
                OnPropertyChanged(nameof(ActiveSessionDiagnostics));
            });
        }
    }

    public string ActiveCpuUsage
    {
        get
        {
            var usage = _lastSnapshot?.CpuMetrics.UsagePercent;
            return usage is >= 0 ? $"{Math.Round(usage.Value)}%" : "Unavailable";
        }
    }

    public string ActiveRamUsage
    {
        get
        {
            var used = _lastSnapshot?.RamMetrics.UsedBytes ?? -1;
            return used >= 0 ? $"{used / (1024.0 * 1024 * 1024):F1} GB" : "Unavailable";
        }
    }

    public string ActiveGpuUsage
    {
        get
        {
            var usage = _lastSnapshot?.GpuMetrics.UsagePercent;
            return usage.HasValue && usage.Value >= 0 ? $"{usage.Value:F0}%" : "Unavailable";
        }
    }
    
    public string ActiveSessionDiagnostics
    {
        get
        {
            if (ActiveSession == null) return "No issues detected.";
            
            return "System performance is optimal.";
        }
    }

    public ICommand AddGameCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand ProfileCommand { get; }
    public ICommand RemoveGameCommand { get; }

    public GameCenterViewModel()
    {
        AddGameCommand = new RelayCommand(_ => AddGameManually());
        PlayCommand = new AsyncRelayCommand(ExecutePlayAsync);
        ProfileCommand = new RelayCommand(ExecuteProfile);
        RemoveGameCommand = new RelayCommand(ExecuteRemove);

        // Setup timer for session duration
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (s, e) => UpdateSessionDuration();

        // Subscribe to session changes and metrics
        App.GameSessionManager.SessionChanged += OnSessionChanged;
        App.MetricsSampler.SnapshotCaptured += OnSnapshotCaptured;

        // Initial load
        LoadProfiles();
        LoadRecentSessions();
        CheckActiveSession();
    }

    /// <summary>
    /// Public refresh hook: SessionsViewModel and other pages may need reloads when the
    /// library changes. Called after profile add/remove/edit.
    /// </summary>
    public void RefreshLibrary()
    {
        LoadProfiles();
        LoadRecentSessions();
    }

    private void LoadRecentSessions()
    {
        RecentSessions.Clear();
        RecentGames.Clear();
        var history = App.ConfigService.GetConfig().SessionHistory;
        
        // Take last 10 sessions ordered by StartTime descending
        foreach (var session in history.OrderByDescending(s => s.StartTime).Take(10))
        {
            RecentSessions.Add(new SessionCard(session));
        }

        // Group by game for Recent Games view (max 5)
        var recentGames = history
            .GroupBy(s => s.GameName)
            .Select(g => new
            {
                GameName = g.Key,
                SessionCount = g.Count(),
                LastDate = g.Max(s => s.EndTime ?? s.StartTime),
                StorageLocation = g.FirstOrDefault()?.GameStorageLocation ?? "Unknown",
                AverageCpu = $"{g.Average(s => s.AverageCpuUsage):F0}%",
                AverageRam = $"{g.Average(s => s.AverageRamUsageBytes) / (1024.0 * 1024 * 1024):F1} GB"
            })
            .OrderByDescending(x => x.LastDate)
            .Take(5);

        foreach (var rg in recentGames)
        {
            RecentGames.Add(new RecentGameCard
            {
                GameName = rg.GameName,
                SessionCount = rg.SessionCount,
                LastPlayedText = FormatLastPlayed(rg.LastDate),
                StorageLocation = rg.StorageLocation,
                AverageCpu = rg.AverageCpu,
                AverageRam = rg.AverageRam
            });
        }
    }

    private string FormatLastPlayed(DateTime date)
    {
        if (date.Date == DateTime.UtcNow.Date) return "Today";
        if (date.Date == DateTime.UtcNow.Date.AddDays(-1)) return "Yesterday";
        return date.ToString("MMM dd");
    }

    private void LoadProfiles()
    {
        _allProfiles.Clear();
        var profiles = App.ConfigService.GetGameProfiles();
        var history = App.ConfigService.GetConfig().SessionHistory;
        
        foreach (var p in profiles.OrderBy(x => x.GameName))
        {
            var card = new ProfileCard { Profile = p };
            
            if (!p.MonitoringEnabled)
            {
                card.ProfileStatus = "PROFILE DISABLED";
            }
            
            var gameSessions = history.Where(s => s.GameId == p.GameId).ToList();
            if (gameSessions.Any())
            {
                card.SessionCount = gameSessions.Count;
                var lastSession = gameSessions.MaxBy(s => s.StartTime);
                if (lastSession != null)
                {
                    card.LastPlayed = FormatLastPlayed(lastSession.StartTime);
                }

                var completedSessions = gameSessions.Where(s => s.EndTime.HasValue).ToList();
                if (completedSessions.Any())
                {
                    var avgTicks = completedSessions.Average(s => (s.EndTime!.Value - s.StartTime).Ticks);
                    var duration = TimeSpan.FromTicks((long)avgTicks);
                    card.TypicalDuration = duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}h {duration.Minutes}m" : $"{duration.Minutes}m";
                    
                    card.AverageCpu = $"{completedSessions.Average(s => s.AverageCpuUsage):F0}%";
                    card.AverageRam = $"{completedSessions.Average(s => s.AverageRamUsageBytes) / (1024.0 * 1024 * 1024):F1} GB";
                }
            }
            else
            {
                if (card.ReadinessState == GameReadinessState.Ready)
                {
                    card.ReadinessState = GameReadinessState.NoHistory;
                    card.ProfileStatus = "READY (NO HISTORY)";
                }
            }
            
            var insights = App.GamingIntelligenceEngine.GetGameInsights(p.GameId);
            foreach(var insight in insights)
            {
                card.GameInsights.Add(insight);
            }
            
            _allProfiles.Add(card);
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilteredProfiles = new ObservableCollection<ProfileCard>(_allProfiles);
        }
        else
        {
            var query = SearchText.ToLowerInvariant();
            FilteredProfiles = new ObservableCollection<ProfileCard>(_allProfiles.Where(p => p.Profile.GameName.ToLowerInvariant().Contains(query)));
        }
    }

    /// <summary>
    /// ADD GAME: opens the Add/Edit dialog where the user browses to ANY valid .exe,
    /// gets a metadata-derived default name they can edit, and saves with validation.
    /// No hardcoded executable names; the path is captured via the Windows file picker.
    /// </summary>
    private void AddGameManually()
    {
        OpenGameEditor(null);
    }

    private void OpenGameEditor(GameProfile? existing)
    {
        if (System.Windows.Application.Current?.MainWindow == null)
        {
            return;
        }

        var editor = new Views.GameEditorWindow(existing);
        editor.ShowDialog();
        if (editor.DialogResult == true)
        {
            LoadProfiles();
            RefreshLibrary();
        }
    }

    private async Task ExecutePlayAsync(object? parameter)
    {
        if (parameter is string gameId)
        {
            if (HasActiveSession)
            {
                if (System.Windows.Application.Current?.MainWindow != null)
                {
                    MessageBox.Show("A game session is already active.", "Game Center", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            var profile = App.ConfigService.GetGameProfiles().FirstOrDefault(p => p.GameId == gameId);
            if (profile != null)
            {
                // Run on background thread to prevent UI freezing
                var session = await Task.Run(() => App.GameSessionManager.StartSessionAsync(profile));
                if (session.CurrentState == GameSessionState.Failed)
                {
                    if (System.Windows.Application.Current?.MainWindow != null)
                    {
                        MessageBox.Show($"Failed to launch game: {session.FailureReason}", "Game Center", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
    }

    private void ExecuteProfile(object? parameter)
    {
        if (parameter is string gameId)
        {
            var profile = App.ConfigService.GetGameProfiles().FirstOrDefault(p => p.GameId == gameId);
            if (profile != null)
            {
                // Full add/edit dialog: name, executable (re-pick via Browse), launch
                // arguments and policies — supersedes the old settings-only editor.
                OpenGameEditor(profile);
            }
        }
    }

    private void ExecuteRemove(object? parameter)
    {
        if (parameter is string gameId)
        {
            var result = System.Windows.Application.Current?.MainWindow != null 
                ? MessageBox.Show("Remove this game profile? This will not delete the actual game.", "Confirm Remove", MessageBoxButton.YesNo) 
                : MessageBoxResult.Yes;
            
            if (result == MessageBoxResult.Yes)
            {
                var config = App.ConfigService.GetConfig();
                config.GameProfiles.RemoveAll(p => p.GameId == gameId);
                App.ConfigService.SaveConfig(config);
                LoadProfiles();
            }
        }
    }

    private void RunOnUIThread(Action action)
    {
        if (Application.Current?.Dispatcher != null)
        {
            if (Application.Current.Dispatcher.Thread == System.Threading.Thread.CurrentThread)
            {
                action();
            }
            else
            {
                try 
                { 
                    // Prevent deadlocks in test environments where dispatcher loop isn't running
                    var operation = Application.Current.Dispatcher.InvokeAsync(action);
                    operation.Wait(TimeSpan.FromMilliseconds(500));
                    if (operation.Status != System.Windows.Threading.DispatcherOperationStatus.Completed) action();
                } 
                catch { action(); }
            }
        }
        else
        {
            action();
        }
    }

    private void OnSessionChanged(object? sender, GameSessionEventArgs e)
    {
        RunOnUIThread(() => 
        {
            CheckActiveSession();
        });
    }

    private void CheckActiveSession()
    {
        var session = App.GameSessionManager.GetActiveSession();
        if (session != null)
        {
            ActiveSession = session;
            _timer.Start();
        }
        else
        {
            if (ActiveSession != null)
            {
                // Session just ended — stand down popups during app shutdown (they race
                // teardown and leave a ghost window). Log instead.
                if (!App.IsShuttingDown && System.Windows.Application.Current?.MainWindow != null)
                {
                    MessageBox.Show("Session ended. Previous system state restored.", "Game Center", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                LoadRecentSessions(); // Refresh recent sessions with the newly ended session
            }
            ActiveSession = null;
            _timer.Stop();
        }
        UpdateSessionDuration();
    }

    private void UpdateSessionDuration()
    {
        if (ActiveSession != null && ActiveSession.StartTime > DateTime.MinValue)
        {
            var diff = DateTime.UtcNow - ActiveSession.StartTime;
            SessionDuration = diff.ToString(@"hh\:mm\:ss");
            // Only update duration on the 1-second tick to avoid excessive layout passes
        }
        else
        {
            SessionDuration = "00:00:00";
        }
    }

}
