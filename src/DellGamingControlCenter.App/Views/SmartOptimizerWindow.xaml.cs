using System.Windows;
namespace DellGamingControlCenter.App.Views
{
    public partial class SmartOptimizerWindow : Window
    {
        public SmartOptimizerWindow() { InitializeComponent(); }

        public SmartOptimizerWindow(DellGamingControlCenter.Core.Models.GameProfile profile)
        {
            InitializeComponent();
            var vm = new ViewModels.SmartOptimizerViewModel();
            vm.Initialize(profile.GameName);
            DataContext = vm;
        }
    }
}
