using System.Windows;

namespace DellGamingControlCenter.App
{
    public partial class MainWindow : Window
    {
        private System.Windows.Forms.NotifyIcon? _notifyIcon;

        public MainWindow()
        {
            InitializeComponent();
            SetupTrayIcon();
        }

        private void SetupTrayIcon()
        {
            // S3: use the real GamerHub icon; fall back to the generic one only if
            // the embedded resource can't be extracted.
            var trayIcon = System.Drawing.SystemIcons.Application;
            try
            {
                // IL3000: Assembly.Location is empty in single-file apps; use AppContext.BaseDirectory for the EXE path.
                var exePath = System.IO.Path.Combine(AppContext.BaseDirectory, AppDomain.CurrentDomain.FriendlyName);
                trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath)
                    ?? System.Drawing.SystemIcons.Application;
            }
            catch
            {
                // keep fallback icon
            }

            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = trayIcon,
                Visible = true,
                Text = "GamerHub"
            };

            _notifyIcon!.DoubleClick += (s, e) =>
            {
                this.Show();
                this.WindowState = WindowState.Normal;
            };

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("Open", null, (s, e) =>
            {
                this.Show();
                this.WindowState = WindowState.Normal;
            });
            contextMenu.Items.Add("Exit", null, (s, e) =>
            {
                if (_notifyIcon != null) {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
                Application.Current.Shutdown();
            });

            _notifyIcon!.ContextMenuStrip = contextMenu;
        }

        /// <summary>
        /// Global search: typing in the top bar filters the Game Center library and
        /// jumps there. No new VM surface — reuses GameCenterViewModel.SearchText
        /// (which already filters FilteredProfiles) and NavigateGameCenterCommand.
        /// </summary>
        private void GlobalSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (DataContext is not ViewModels.MainViewModel vm || sender is not System.Windows.Controls.TextBox tb)
                return;

            vm.GameCenterViewModel.SearchText = tb.Text ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(tb.Text) &&
                !ReferenceEquals(vm.CurrentViewModel, vm.GameCenterViewModel))
            {
                vm.NavigateGameCenterCommand.Execute(null);
            }
        }

        protected override void OnStateChanged(System.EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                this.Hide();
            }
            base.OnStateChanged(e);
        }

        protected override void OnClosed(System.EventArgs e)
        {
            if (_notifyIcon != null) {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            base.OnClosed(e);
        }
    }
}
