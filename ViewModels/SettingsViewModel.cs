using System.IO;
using System.Windows.Input;
using SimpleMirror.Models;
using SimpleMirror.Services;

namespace SimpleMirror.ViewModels;

/// <summary>
/// 設定画面のViewModel
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private PerformanceProfile _profile;
    private string _serverName;
    private int _port;
    private bool _keepAspectRatio;
    private bool _enableAudio;
    private bool _enableAutoOrientation;
    private string _screenshotDirectory;
    private bool _copyScreenshotToClipboard;
    private bool _autoStartServer;

    public PerformanceProfile Profile
    {
        get => _profile;
        set
        {
            if (SetProperty(ref _profile, value))
            {
                OnPropertyChanged(nameof(IsPerformanceProfile));
                OnPropertyChanged(nameof(IsBalancedProfile));
                OnPropertyChanged(nameof(IsQualityProfile));
            }
        }
    }

    public bool IsPerformanceProfile
    {
        get => _profile == PerformanceProfile.Performance;
        set { if (value) Profile = PerformanceProfile.Performance; }
    }

    public bool IsBalancedProfile
    {
        get => _profile == PerformanceProfile.Balanced;
        set { if (value) Profile = PerformanceProfile.Balanced; }
    }

    public bool IsQualityProfile
    {
        get => _profile == PerformanceProfile.Quality;
        set { if (value) Profile = PerformanceProfile.Quality; }
    }

    public string ServerName
    {
        get => _serverName;
        set => SetProperty(ref _serverName, value);
    }

    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    public bool KeepAspectRatio
    {
        get => _keepAspectRatio;
        set => SetProperty(ref _keepAspectRatio, value);
    }

    public bool EnableAudio
    {
        get => _enableAudio;
        set => SetProperty(ref _enableAudio, value);
    }

    public bool EnableAutoOrientation
    {
        get => _enableAutoOrientation;
        set => SetProperty(ref _enableAutoOrientation, value);
    }

    public string ScreenshotDirectory
    {
        get => _screenshotDirectory;
        set => SetProperty(ref _screenshotDirectory, value);
    }

    public bool CopyScreenshotToClipboard
    {
        get => _copyScreenshotToClipboard;
        set => SetProperty(ref _copyScreenshotToClipboard, value);
    }

    public bool AutoStartServer
    {
        get => _autoStartServer;
        set => SetProperty(ref _autoStartServer, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand BrowseDirectoryCommand { get; }
    public ICommand ResetDefaultsCommand { get; }

    public event Action? RequestClose;

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        var s = _settingsService.CurrentSettings;

        _profile = s.Profile;
        _serverName = s.ServerName;
        _port = s.Port;
        _keepAspectRatio = s.KeepAspectRatio;
        _enableAudio = s.EnableAudio;
        _enableAutoOrientation = s.EnableAutoOrientation;
        _screenshotDirectory = s.ScreenshotDirectory;
        _copyScreenshotToClipboard = s.CopyScreenshotToClipboard;
        _autoStartServer = s.AutoStartServer;

        SaveCommand = new RelayCommand(Save);
        BrowseDirectoryCommand = new RelayCommand(BrowseDirectory);
        ResetDefaultsCommand = new RelayCommand(ResetDefaults);
    }

    private void Save()
    {
        var s = _settingsService.CurrentSettings;
        s.Profile = Profile;
        s.ServerName = ServerName;
        s.Port = Port;
        s.KeepAspectRatio = KeepAspectRatio;
        s.EnableAudio = EnableAudio;
        s.EnableAutoOrientation = EnableAutoOrientation;
        s.ScreenshotDirectory = ScreenshotDirectory;
        s.CopyScreenshotToClipboard = CopyScreenshotToClipboard;
        s.AutoStartServer = AutoStartServer;

        _settingsService.Save();
        RequestClose?.Invoke();
    }

    private void BrowseDirectory()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "スクリーンショットの保存先フォルダを選択",
            InitialDirectory = Directory.Exists(ScreenshotDirectory) ? ScreenshotDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };

        if (dialog.ShowDialog() == true)
        {
            ScreenshotDirectory = dialog.FolderName;
        }
    }

    private void ResetDefaults()
    {
        var def = new AppSettings();
        Profile = def.Profile;
        ServerName = def.ServerName;
        Port = def.Port;
        KeepAspectRatio = def.KeepAspectRatio;
        EnableAudio = def.EnableAudio;
        EnableAutoOrientation = def.EnableAutoOrientation;
        ScreenshotDirectory = def.ScreenshotDirectory;
        CopyScreenshotToClipboard = def.CopyScreenshotToClipboard;
        AutoStartServer = def.AutoStartServer;
    }
}
