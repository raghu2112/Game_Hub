namespace DellGamingControlCenter.App.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Linq;
using System.Text;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Interfaces;

public class SessionItem : BaseViewModel
{
    public string Game { get; set; } = "";
    public string Date { get; set; } = "";
    public string Duration { get; set; } = "";
    public int Health { get; set; }
    public string PrimaryBottleneck { get; set; } = "";
    public string Optimization { get; set; } = "";
    public string Restoration { get; set; } = "";

    public ObservableCollection<GamingInsight> SessionInsights { get; } = new();
}

public class SessionsViewModel : BaseViewModel
{
    public ObservableCollection<SessionItem> Sessions { get; } = new();

    public ICommand ViewReportCommand { get; }
    public ICommand RefreshCommand { get; }

    public SessionsViewModel()
    {
        ViewReportCommand = new RelayCommand(ExecuteViewReport);
        RefreshCommand = new RelayCommand(_ => LoadSessions());

        LoadSessions();

        // B3: the page previously loaded once in the constructor and never refreshed,
        // so finished sessions never appeared until the app was restarted.
        App.GameSessionManager.SessionChanged += OnSessionChanged;
    }

    private void OnSessionChanged(object? sender, GameSessionEventArgs e)
    {
        // Handler may fire on a background thread; marshal through the shared helper.
        UiThread.Run(() => LoadSessions());
    }

    public void LoadSessions()
    {
        Sessions.Clear();
        var history = App.ConfigService.GetConfig().SessionHistory
            .OrderByDescending(s => s.StartTime)
            .ToList();
        foreach (var session in history)
        {
            var item = new SessionItem
            {
                Game = session.GameName,
                Date = session.StartTime.ToLocalTime().ToShortDateString(),
                Duration = session.EndTime.HasValue ? (session.EndTime.Value - session.StartTime).ToString(@"hh\:mm") : "Ongoing",
                Health = session.Report?.HealthScore ?? 0,
                PrimaryBottleneck = session.Report?.Bottlenecks.FirstOrDefault()?.Type.ToString() ?? "None",
                Optimization = session.OptimizationSuccessful ? "Applied" : "Failed",
                Restoration = session.RestorationSuccessful ? "Successful" : "Warnings"
            };

            var insights = App.GamingIntelligenceEngine.GetSessionInsights(session);
            foreach (var insight in insights)
            {
                item.SessionInsights.Add(insight);
            }

            Sessions.Add(item);
        }
    }

    private void ExecuteViewReport(object? parameter)
    {
        // B4: this was a MessageBox placeholder. Resolve the real GameSession and open
        // an actual report window with the recorded telemetry and timeline.
        if (parameter is not SessionItem item)
        {
            return;
        }

        var session = App.ConfigService.GetConfig().SessionHistory
            .Where(s => s.GameName == item.Game)
            .OrderByDescending(s => s.StartTime)
            .FirstOrDefault();

        if (session == null)
        {
            MessageBox.Show("The detailed record for this session is no longer available.", "Session Report",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new Views.SessionReportWindow(session) { Owner = Application.Current?.MainWindow };
        window.ShowDialog();
    }
}

/// <summary>
/// B4: real session report window replacing the previous placeholder MessageBox.
/// Shows recorded telemetry, applied policies, timeline and post-session analysis.
/// </summary>
public static class UiThread
{
    public static void Run(Action action)
    {
        var app = Application.Current;
        if (app?.Dispatcher == null)
        {
            action();
            return;
        }
        if (app.Dispatcher.Thread == System.Threading.Thread.CurrentThread)
        {
            action();
        }
        else
        {
            app.Dispatcher.BeginInvoke(action);
        }
    }
}
