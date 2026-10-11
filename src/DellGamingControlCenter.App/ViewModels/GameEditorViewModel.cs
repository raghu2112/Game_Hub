namespace DellGamingControlCenter.App.ViewModels;

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Models;

/// <summary>
/// View model for the Add/Edit Game dialog. Handles the complete workflow:
/// browse to any valid .exe → derive a default name from file metadata →
/// edit display name / launch arguments / hardware policies → validate → save.
/// Validation rules live in <see cref="GameLibraryValidator"/> (pure, unit-tested).
/// </summary>
public class GameEditorViewModel : BaseViewModel
{
    private static readonly JsonSerializerOptions JsonOpts = new();

    private readonly GameProfile _working;
    private readonly string? _gameId;          // null while adding a new game
    private readonly Action _onSaved;
    private readonly Action _onCancelled;
    private string _autoName = "";             // last metadata-derived name (so Browse can refresh it until the user edits it)

    public string Title { get; }

    public string GameName
    {
        get => _gameName;
        set { _gameName = value; OnPropertyChanged(); }
    }
    private string _gameName = "";

    public string ExecutablePath
    {
        get => _executablePath;
        private set { _executablePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasExecutable)); }
    }
    private string _executablePath = "";

    /// <summary>Placeholder visible until an executable is picked via Browse.</summary>
    public bool HasExecutable => !string.IsNullOrEmpty(_executablePath);

    public string LaunchArguments
    {
        get => _working.LaunchArguments ?? "";
        set { _working.LaunchArguments = value; OnPropertyChanged(); }
    }

    public IEnumerable<GpuPreference> GpuPreferences => Enum.GetValues<GpuPreference>();
    public IEnumerable<PerformanceMode> PerformanceModes => Enum.GetValues<PerformanceMode>();
    public IEnumerable<ProcessPriorityClass> ProcessPriorities => Enum.GetValues<ProcessPriorityClass>();

    public GpuPreference SelectedGpuPreference
    {
        get => _working.PreferredGpu;
        set { _working.PreferredGpu = value; OnPropertyChanged(); }
    }

    public PerformanceMode SelectedPerformanceMode
    {
        get => _working.PerformanceMode;
        set { _working.PerformanceMode = value; OnPropertyChanged(); }
    }

    public ProcessPriorityClass SelectedProcessPriority
    {
        get => _working.ProcessPriority;
        set { _working.ProcessPriority = value; OnPropertyChanged(); }
    }

    public bool IsGpuPreferenceSupported => App.GpuPreferenceController.Status == CapabilityStatus.Supported;
    public string FanControlStatus => "Not supported on this device";
    public string ThermalControlStatus => "Not supported on this device";

    public string ValidationMessage
    {
        get => _validationMessage;
        set { _validationMessage = value; OnPropertyChanged(); }
    }
    private string _validationMessage = "";

    public ICommand BrowseCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public GameEditorViewModel(GameProfile? existing, Action onSaved, Action onCancelled)
    {
        _onSaved = onSaved;
        _onCancelled = onCancelled;

        if (existing != null)
        {
            // Edit: work on a detached clone so Cancel cannot half-mutate the stored profile.
            _working = JsonSerializer.Deserialize<GameProfile>(JsonSerializer.Serialize(existing, JsonOpts), JsonOpts) ?? new GameProfile();
            _gameId = existing.GameId;
            // Suppress: the initial DataContext binding fires TextChanged; without this the
            // name would be pre-marked as user-edited and Browse could never refresh it.
            SetNameSilently(existing.GameName);
            _executablePath = existing.ExecutablePath;
            _autoName = existing.GameName;
            Title = "Edit Game";
        }
        else
        {
            _working = new GameProfile { GameId = Guid.NewGuid().ToString("N")[..8] };
            Title = "Add Game";
        }

        BrowseCommand = new RelayCommand(_ => Browse());
        SaveCommand = new RelayCommand(_ => Save());
        CancelCommand = new RelayCommand(_ => _onCancelled());
    }

    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "Select Game Executable",
            CheckFileExists = true
        };

        // Any valid Windows game executable — no hardcoded file names.
        if (dialog.ShowDialog() != true) return;

        ExecutablePath = dialog.FileName;

        // Derive a sensible default display name from file metadata. Keep a name the
        // user typed, but refresh one we auto-derived from a previous Browse.
        var derived = GameLibraryValidator.DeriveDefaultName(dialog.FileName);
        if (string.IsNullOrWhiteSpace(_gameName) || (!_nameEditedByUser && string.Equals(_gameName, _autoName, StringComparison.Ordinal)))
        {
            // SetNameSilently: without suppression the resulting TextChanged would mark the
            // name as user-edited, so a SECOND Browse could never refresh the auto-name.
            SetNameSilently(derived);
            _autoName = derived;
        }
    }

    private bool _nameEditedByUser;
    private bool _suppressNameEdited;

    /// <summary>Called by the view when the user edits the name box, so Browse stops overwriting it. Ignores programmatic updates.</summary>
    public void MarkNameEdited()
    {
        if (!_suppressNameEdited) _nameEditedByUser = true;
    }

    private void SetNameSilently(string value)
    {
        _suppressNameEdited = true;
        try
        {
            _gameName = value;
            OnPropertyChanged(nameof(GameName));
        }
        finally
        {
            _suppressNameEdited = false;
        }
    }

    private void Save()
    {
        ValidationMessage = "";

        var error = GameLibraryValidator.ValidateForSave(
            _gameName, _executablePath, _gameId, App.ConfigService.GetGameProfiles());
        if (error != null)
        {
            ValidationMessage = error;
            return;
        }

        if (!IsGpuPreferenceSupported && _working.PreferredGpu != GpuPreference.Auto)
        {
            _working.PreferredGpu = GpuPreference.Auto;
            OnPropertyChanged(nameof(SelectedGpuPreference));
        }

        // GameProfile's identity fields are init-only: override them through a JSON
        // round-trip so every other setting on the clone is preserved exactly.
        var node = JsonNode.Parse(JsonSerializer.Serialize(_working, JsonOpts))!;
        node[nameof(GameProfile.GameName)] = _gameName.Trim();
        node[nameof(GameProfile.ExecutablePath)] = _executablePath;
        node[nameof(GameProfile.ModifiedAt)] = DateTime.UtcNow;
        var final = node.Deserialize<GameProfile>(JsonOpts) ?? _working;

        App.ConfigService.SaveGameProfile(final);
        _onSaved();
    }

    internal void Cancel() => _onCancelled();
}
