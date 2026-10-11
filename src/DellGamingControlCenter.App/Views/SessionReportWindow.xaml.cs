using System.Windows;

namespace DellGamingControlCenter.App.Views
{
    public partial class SessionReportWindow : Window
    {
        public SessionReportWindow(Core.Models.GameSession session)
        {
            InitializeComponent();
            Populate(session);
        }

        private void Populate(Core.Models.GameSession session)
        {
            var state = session.CurrentState.ToString();
            HeaderText.Text = $"{session.GameName} — {state}";
            SummaryText.Text = $"Started {session.StartTime.ToLocalTime():g}" +
                               (session.EndTime.HasValue ? $" • Ended {session.EndTime.Value.ToLocalTime():g}" : " • Still running") +
                               (string.IsNullOrEmpty(session.FailureReason) ? "" : $"\nFailure: {session.FailureReason}");

            var metrics = new System.Text.StringBuilder();
            metrics.AppendLine($"Average CPU: {session.AverageCpuUsage:F0}%   Peak CPU: {session.PeakCpuUsage:F0}%");
            metrics.AppendLine($"Average RAM: {session.AverageRamUsageBytes / (1024.0 * 1024 * 1024):F1} GB   Peak RAM: {session.PeakRamUsageBytes / (1024.0 * 1024 * 1024):F1} GB");
            if (session.PeakGpuUsage > 0)
            {
                metrics.AppendLine($"Peak GPU: {session.PeakGpuUsage:F0}%");
            }
            else
            {
                metrics.AppendLine("Peak GPU: Unavailable on this hardware");
            }
            metrics.Append($"Storage location: {session.GameStorageLocation}");
            MetricsText.Text = metrics.ToString();

            PoliciesText.Text = session.AppliedChanges.Count == 0
                ? "No policies were applied for this session."
                : string.Join("\n", session.AppliedChanges.Select(a => $"• {a.Description}: {a.Value}"));

            RestorationText.Text = session.OptimizationSuccessful switch
            {
                true when session.RestorationSuccessful => "Policies applied and system state fully restored.",
                true => "Policies applied; restoration reported warnings.",
                false => "Policy application failed or was skipped; nothing needed restoring."
            };

            TimelineList.ItemsSource = session.TimelineEvents;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
