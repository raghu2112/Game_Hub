namespace DellGamingControlCenter.App.Views;

using System.Windows;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.App.ViewModels;

public partial class ProfileEditorWindow : Window
{
    public ProfileEditorWindow(GameProfile profile)
    {
        InitializeComponent();
        DataContext = new ProfileEditorViewModel(profile, 
            onSave: () => Close(),
            onCancel: () => Close()
        );
    }
}
