namespace DellGamingControlCenter.App.ViewModels;

using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Services;

public class BenchmarkViewModel : BaseViewModel
{
    private readonly IBenchmarkService _benchmarkService;
    private readonly IGameTuningService _tuningService;
    private readonly IGameDiscoveryService _discoveryService;
    private readonly IGameSessionManager _sessionManager;

    private string _statusText = "Ready";
    private bool _isRunning;
    private BenchmarkRun? _currentRun;
    private System.Threading.CancellationTokenSource? _cts;
    
    private string _selectedGameId = "";
    private string _bestHealthScore = "N/A";
    private string _primaryBottleneck = "None";
    private string _confidence = "Low";
    
    public ObservableCollection<DiscoveredGame> DiscoveredGames { get; } = new();
    public ObservableCollection<BenchmarkRun> BenchmarkHistory { get; } = new();
    
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public bool IsRunning { get => _isRunning; set => SetProperty(ref _isRunning, value); }
    
    public string SelectedGameId 
    { 
        get => _selectedGameId; 
        set 
        { 
            if (SetProperty(ref _selectedGameId, value))
            {
                _ = LoadGameDataAsync();
            }
        } 
    }
    
    public string BestHealthScore { get => _bestHealthScore; set => SetProperty(ref _bestHealthScore, value); }
    public string PrimaryBottleneck { get => _primaryBottleneck; set => SetProperty(ref _primaryBottleneck, value); }
    public string Confidence { get => _confidence; set => SetProperty(ref _confidence, value); }

    public ICommand StartBaselineCommand { get; }
    public ICommand StartTunedCommand { get; }
    public ICommand CancelCommand { get; }
    
    public BenchmarkViewModel(IBenchmarkService benchmarkService, IGameTuningService tuningService, IGameDiscoveryService discoveryService, IGameSessionManager sessionManager)
    {
        _benchmarkService = benchmarkService;
        _tuningService = tuningService;
        _discoveryService = discoveryService;
        _sessionManager = sessionManager;
        
        StartBaselineCommand = new AsyncRelayCommand(async _ => await RunBenchmarkAsync(true), _ => !IsRunning && !string.IsNullOrEmpty(SelectedGameId));
        StartTunedCommand = new AsyncRelayCommand(async _ => await RunBenchmarkAsync(false), _ => !IsRunning && !string.IsNullOrEmpty(SelectedGameId));
        CancelCommand = new RelayCommand(_ => CancelBenchmark(), _ => IsRunning);
        
        LoadGames();
    }

    /// <summary>
    /// Re-entry refresh: the benchmark list must include games the user added in
    /// Game Center after startup (pages are singletons, so the constructor merge
    /// alone leaves the list stale). Never mutates the list under an active run.
    /// </summary>
    public void RefreshGames()
    {
        if (IsRunning) return;
        LoadGames();
    }
    
    private void LoadGames()
    {
        DiscoveredGames.Clear();

        // B7: the list previously came only from the discovery cache (hard-coded search
        // paths), so games the user added in Game Center never appeared and the benchmark
        // buttons looked dead. Merge both sources: user profiles first, then discovery cache.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in App.ConfigService.GetGameProfiles())
        {
            if (string.IsNullOrWhiteSpace(p.ExecutablePath) || !seen.Add(p.ExecutablePath))
            {
                continue;
            }
            DiscoveredGames.Add(new DiscoveredGame
            {
                Id = p.GameId,
                Name = p.GameName,
                ExecutablePath = p.ExecutablePath,
                InstallDirectory = p.InstallDirectory,
                ProfileState = ProfileState.Configured
            });
        }
        foreach (var g in _discoveryService.GetCachedGames())
        {
            if (string.IsNullOrWhiteSpace(g.ExecutablePath) || !seen.Add(g.ExecutablePath))
            {
                continue;
            }
            //, keep the discovery entry within the same Id namespace as its profile when one exists
            var matchedProfile = App.ConfigService.GetGameProfiles().FirstOrDefault(p =>
                string.Equals(p.ExecutablePath, g.ExecutablePath, StringComparison.OrdinalIgnoreCase));
            DiscoveredGames.Add(new DiscoveredGame
            {
                Id = matchedProfile?.GameId ?? g.Id,
                Name = g.Name,
                ExecutablePath = g.ExecutablePath,
                InstallDirectory = g.InstallDirectory,
            });
        }

        if (DiscoveredGames.Any())
        {
            SelectedGameId = DiscoveredGames.First().Id;
        }
        else
        {
            StatusText = "No games to benchmark yet. Add a game in Game Center first.";
        }
    }
    
    private async Task LoadGameDataAsync()
    {
        if (string.IsNullOrEmpty(SelectedGameId)) return;
        
        var history = await _benchmarkService.GetHistoryAsync(SelectedGameId);
        BenchmarkHistory.Clear();
        foreach (var h in history) BenchmarkHistory.Add(h);
        
        var tuning = await _tuningService.GetPerformanceHistoryAsync(SelectedGameId);
        BestHealthScore = tuning.BestHealthScore > 0 ? tuning.BestHealthScore.ToString() : "N/A";
        PrimaryBottleneck = tuning.PrimaryBottleneck;
        Confidence = tuning.BottleneckConfidence;
    }
    
    private void CancelBenchmark()
    {
        _cts?.Cancel();
        StatusText = "Cancelling benchmark...";
    }

    private async Task RunBenchmarkAsync(bool isBaseline)
    {
        var game = DiscoveredGames.FirstOrDefault(g => g.Id == SelectedGameId);
        if (game == null) return;
        
        IsRunning = true;
        StatusText = $"Preparing {(isBaseline ? "Baseline" : "Tuned")} benchmark for {game.Name}...";
        _cts = new System.Threading.CancellationTokenSource();
        
        try
        {
            var profile = new GameProfile { GameId = game.Id, GameName = game.Name, ExecutablePath = game.ExecutablePath };
            _currentRun = await _benchmarkService.StartBenchmarkAsync(profile, BenchmarkType.Standard, isBaseline);
            
            // Run real session on background thread to prevent WMI blocking the UI
            StatusText = $"Launching {game.Name}...";
            var session = await Task.Run(() => _sessionManager.StartSessionAsync(profile));
            
            if (session.CurrentState == GameSessionState.Failed)
            {
                StatusText = $"Launch Failed: {session.FailureReason}";
                return;
            }

            StatusText = $"Monitoring {game.Name}... (Play the game, close when done)";

            // Wait for session to finish or cancellation
            while (session.CurrentState != GameSessionState.Completed && 
                   session.CurrentState != GameSessionState.CompletedWithWarnings && 
                   session.CurrentState != GameSessionState.Failed && 
                   session.CurrentState != GameSessionState.Cancelled)
            {
                if (_cts.IsCancellationRequested)
                {
                    StatusText = "Stopping monitoring...";
                    await _sessionManager.StopSessionAsync(session.SessionId);
                    break;
                }
                await Task.Delay(1000);
            }
            
            if (session.Report != null && !_cts.IsCancellationRequested)
            {
                StatusText = "Analyzing data...";
                await _benchmarkService.StopBenchmarkAsync(_currentRun, session.Report);
                StatusText = $"Benchmark complete. Health: {session.Report.HealthScore}";
            }
            else
            {
                StatusText = _cts.IsCancellationRequested ? "Benchmark cancelled." : "Benchmark failed to generate report.";
            }
            await LoadGameDataAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }
}
