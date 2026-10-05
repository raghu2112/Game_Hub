namespace DellGamingControlCenter.App.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Linq;

public class SessionItem : BaseViewModel
{
    public string Game { get; set; } = "";
    public string Date { get; set; } = "";
    public string Duration { get; set; } = "";
    public int Health { get; set; }
    public string PrimaryBottleneck { get; set; } = "";
    public string Optimization { get; set; } = "";
    public string Restoration { get; set; } = "";
}

public class SessionsViewModel : BaseViewModel
{
    public ObservableCollection<SessionItem> Sessions { get; } = new();

    public ICommand ViewReportCommand { get; }

    public SessionsViewModel()
    {
        ViewReportCommand = new RelayCommand(ExecuteViewReport);

        LoadSessions();
    }

    private void LoadSessions()
    {
        Sessions.Clear();
        var history = App.ConfigService.GetConfig().SessionHistory;
        foreach(var session in history)
        {
            Sessions.Add(new SessionItem 
            { 
                Game = session.GameName, 
                Date = session.StartTime.ToShortDateString(), 
                Duration = session.EndTime.HasValue ? (session.EndTime.Value - session.StartTime).ToString(@"hh\:mm") : "Ongoing", 
                Health = session.Report?.HealthScore ?? 0, 
                PrimaryBottleneck = session.Report?.Bottlenecks.FirstOrDefault()?.Type.ToString() ?? "None", 
                Optimization = session.OptimizationSuccessful ? "Applied" : "Failed", 
                Restoration = session.RestorationSuccessful ? "Successful" : "Warnings" 
            });
        }
    }

    private void ExecuteViewReport(object? parameter)
    {
        if (parameter is SessionItem session)
        {
            System.Windows.MessageBox.Show($"Opening report for {session.Game} session...", "Session Report");
        }
    }
}
