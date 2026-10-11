namespace DellGamingControlCenter.App.Views;

using System.Windows;
using System.Windows.Controls;
using DellGamingControlCenter.Core.Models;
using ViewModels;

public partial class GameEditorWindow : Window
{
    private readonly GameEditorViewModel _vm;

    public GameEditorWindow(GameProfile? existing)
    {
        InitializeComponent();
        _vm = new GameEditorViewModel(
            existing,
            onSaved: () => { DialogResult = true; Close(); },
            onCancelled: () => { DialogResult = false; Close(); });
        DataContext = _vm;
        Owner = Application.Current?.MainWindow;
        Loaded += (_, _) =>
        {
            // Start the user in the primary field (name) instead of an unfocused dialog.
            GameNameBox.Focus();
            GameNameBox.CaretIndex = GameNameBox.Text.Length;
        };
    }

    private void GameName_TextChanged(object sender, TextChangedEventArgs e)
        => _vm.MarkNameEdited();
}
