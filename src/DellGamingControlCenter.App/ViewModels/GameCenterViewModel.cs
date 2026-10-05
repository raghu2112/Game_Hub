namespace DellGamingControlCenter.App.ViewModels;

using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using DellGamingControlCenter.Core.Models;

public class GameCard : BaseViewModel
{
    public DiscoveredGame Game { get; set; } = null!;
    
    private int _benchmarkCount;
    private int _bestHealthScore;
    private string _primaryBottleneck = "None";
    private string _confidence = "Low";
    private string _benchmarkStatus = "Untested";
    
    public int BenchmarkCount { get => _benchmarkCount; set => SetProperty(ref _benchmarkCount, value); }
    public int BestHealthScore { get => _bestHealthScore; set => SetProperty(ref _bestHealthScore, value); }
    public string PrimaryBottleneck { get => _primaryBottleneck; set => SetProperty(ref _primaryBottleneck, value); }
    public string Confidence { get => _confidence; set => SetProperty(ref _confidence, value); }
    public string BenchmarkStatus { get => _benchmarkStatus; set => SetProperty(ref _benchmarkStatus, value); }
}

public class GameCenterViewModel : BaseViewModel
{
    private ObservableCollection<GameCard> _allGames = new();
    private ObservableCollection<GameCard> _filteredGames = new();
    private string _searchText = "";
    private bool _isScanning;

    public ObservableCollection<GameCard> FilteredGames
    {
        get => _filteredGames;
        set => SetProperty(ref _filteredGames, value);
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

    public bool IsScanning
    {
        get => _isScanning;
        set => SetProperty(ref _isScanning, value);
    }

    public ICommand ScanCommand { get; }
    public ICommand AddGameCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand OptimizeCommand { get; }
    public ICommand ProfileCommand { get; }

    public GameCenterViewModel()
    {
        ScanCommand = new AsyncRelayCommand(async _ => await ScanGamesAsync(true), _ => !IsScanning);
        AddGameCommand = new RelayCommand(_ => AddGameManually());
        PlayCommand = new AsyncRelayCommand(ExecutePlayAsync);
        OptimizeCommand = new RelayCommand(ExecuteOptimize);
        ProfileCommand = new RelayCommand(ExecuteProfile);

        // Initial load
        _ = ScanGamesAsync(false);
    }

    private async Task ScanGamesAsync(bool forceRescan)
    {
        if (IsScanning) return;
        IsScanning = true;

        try
        {
            var games = await App.GameDiscovery.ScanForGamesAsync(forceRescan);
            _allGames.Clear();
            
            foreach(var g in games.OrderBy(x => x.Name))
            {
                var card = new GameCard { Game = g };
                var history = await App.GameTuningService.GetPerformanceHistoryAsync(g.Id);
                
                card.BenchmarkCount = history.BenchmarkCount;
                card.BestHealthScore = history.BestHealthScore;
                card.PrimaryBottleneck = history.PrimaryBottleneck;
                card.Confidence = history.BottleneckConfidence;
                card.BenchmarkStatus = history.BenchmarkCount > 0 ? $"{history.BenchmarkCount} runs" : "Untested";
                
                _allGames.Add(card);
            }
            ApplyFilter();
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilteredGames = new ObservableCollection<GameCard>(_allGames);
        }
        else
        {
            var query = SearchText.ToLowerInvariant();
            FilteredGames = new ObservableCollection<GameCard>(_allGames.Where(g => g.Game.Name.ToLowerInvariant().Contains(query)));
        }
    }

    private void AddGameManually()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Executables (*.exe)|*.exe",
            Title = "Select Game Executable"
        };

        if (dialog.ShowDialog() == true)
        {
            App.GameDiscovery.AddGameManually(dialog.FileName);
            _ = ScanGamesAsync(false);
        }
    }

    private async Task ExecutePlayAsync(object? parameter)
    {
        if (parameter is string gameId)
        {
            var game = _allGames.FirstOrDefault(g => g.Game.Id == gameId)?.Game;
            if (game != null)
            {
                var profile = new GameProfile { GameId = game.Id, GameName = game.Name, ExecutablePath = game.ExecutablePath };
                // Run on background thread to prevent UI freezing from WMI
                var session = await Task.Run(() => App.GameSessionManager.StartSessionAsync(profile));
                if (session.CurrentState == Core.Enums.GameSessionState.Failed)
                {
                    System.Windows.MessageBox.Show($"Failed to launch game: {session.FailureReason}", "Game Center", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }
    }

    private void ExecuteOptimize(object? parameter)
    {
        if (parameter is string gameId)
        {
            var game = _allGames.FirstOrDefault(g => g.Game.Id == gameId)?.Game;
            if (game != null)
            {
                var profile = new GameProfile { GameId = game.Id, GameName = game.Name, ExecutablePath = game.ExecutablePath };
                var smartOpt = new Views.SmartOptimizerWindow(profile);
                smartOpt.ShowDialog();
            }
        }
    }

    private void ExecuteProfile(object? parameter)
    {
        if (parameter is string gameId)
        {
            App.MainViewModel.NavigateBenchmarksCommand.Execute(null);
            App.MainViewModel.CurrentViewModel = App.BenchmarkViewModel;
            App.BenchmarkViewModel.SelectedGameId = gameId;
        }
    }
}
