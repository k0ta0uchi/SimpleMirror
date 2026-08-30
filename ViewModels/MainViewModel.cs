using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SimpleMirror.Models;
using SimpleMirror.Services;

namespace SimpleMirror.ViewModels;

/// <summary>
/// メイン画面のViewModel
/// </summary>
public class MainViewModel : ViewModelBase, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly AirPlayEngineManager _engineManager;
    private readonly CaptureService _captureService;
    private readonly FirewallService _firewallService;
    private readonly DispatcherTimer _toastTimer;

    private ConnectionStatus _status = ConnectionStatus.Stopped;
    private MirrorSessionInfo? _sessionInfo;
    private bool _alwaysOnTop;
    private bool _isFullscreen;
    private bool _keepAspectRatio = true;
    private string _statusText = "待機中";
    private string _toastMessage = string.Empty;
    private bool _isToastVisible;
    private bool _isFirewallConfigured = true;
    private IntPtr _currentVideoHwnd = IntPtr.Zero;

    public ConnectionStatus Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(IsListening));
                OnPropertyChanged(nameof(IsStopped));
                UpdateStatusText();
            }
        }
    }

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsListening => Status == ConnectionStatus.Listening;
    public bool IsStopped => Status == ConnectionStatus.Stopped;

    public MirrorSessionInfo? SessionInfo
    {
        get => _sessionInfo;
        private set => SetProperty(ref _sessionInfo, value);
    }

    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set
        {
            if (SetProperty(ref _alwaysOnTop, value))
            {
                _settingsService.CurrentSettings.AlwaysOnTop = value;
                _settingsService.Save();
            }
        }
    }

    public bool IsFullscreen
    {
        get => _isFullscreen;
        set => SetProperty(ref _isFullscreen, value);
    }

    public bool KeepAspectRatio
    {
        get => _keepAspectRatio;
        set => SetProperty(ref _keepAspectRatio, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ToastMessage
    {
        get => _toastMessage;
        private set => SetProperty(ref _toastMessage, value);
    }

    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => SetProperty(ref _isToastVisible, value);
    }

    public bool IsFirewallConfigured
    {
        get => _isFirewallConfigured;
        private set => SetProperty(ref _isFirewallConfigured, value);
    }

    public string ServerName => _settingsService.CurrentSettings.ServerName;
    public int Port => _settingsService.CurrentSettings.Port;
    public string ActiveIpAddress => _engineManager.ActiveIpAddress;
    public bool IsBonjourAvailable => _engineManager.IsBonjourAvailable;

    private bool _isCleanView;
    private bool _enableAutoOrientation;

    public bool IsCleanView
    {
        get => _isCleanView;
        set => SetProperty(ref _isCleanView, value);
    }

    public bool EnableAutoOrientation
    {
        get => _enableAutoOrientation;
        set
        {
            if (SetProperty(ref _enableAutoOrientation, value))
            {
                _settingsService.CurrentSettings.EnableAutoOrientation = value;
                _settingsService.Save();
                ShowToast(value ? "自動回転: オン" : "自動回転: オフ");
            }
        }
    }

    public ICommand ToggleAlwaysOnTopCommand { get; }
    public ICommand ToggleFullscreenCommand { get; }
    public ICommand ToggleCleanViewCommand { get; }
    public ICommand EscapeCommand { get; }
    public ICommand ToggleOrientationCommand { get; }
    public ICommand TakeScreenshotCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand RestartServerCommand { get; }
    public ICommand ToggleServerCommand { get; }
    public ICommand OpenScreenshotFolderCommand { get; }
    public ICommand ConfigureFirewallCommand { get; }

    public event Action? RequestOpenSettings;
    public event Action? RequestOrientationToggle;
    public event Action<bool>? RequestOrientationChange;
    public event Action<IntPtr>? AttachVideoWindowRequested;
    public event Action? DetachVideoWindowRequested;
    public event Action<int, int>? VideoDimensionsChanged;

    public MainViewModel(
        SettingsService settingsService,
        AirPlayEngineManager engineManager,
        CaptureService captureService,
        FirewallService firewallService)
    {
        _settingsService = settingsService;
        _engineManager = engineManager;
        _captureService = captureService;
        _firewallService = firewallService;

        _alwaysOnTop = _settingsService.CurrentSettings.AlwaysOnTop;
        _keepAspectRatio = _settingsService.CurrentSettings.KeepAspectRatio;
        _enableAutoOrientation = _settingsService.CurrentSettings.EnableAutoOrientation;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (s, e) =>
        {
            IsToastVisible = false;
            _toastTimer.Stop();
        };

        // コマンド初期化
        ToggleAlwaysOnTopCommand = new RelayCommand(() => AlwaysOnTop = !AlwaysOnTop);
        ToggleFullscreenCommand = new RelayCommand(() => IsFullscreen = !IsFullscreen);
        ToggleCleanViewCommand = new RelayCommand(() => IsCleanView = !IsCleanView);
        EscapeCommand = new RelayCommand(HandleEscape);
        ToggleOrientationCommand = new RelayCommand(ToggleOrientation);
        TakeScreenshotCommand = new RelayCommand(async () => await TakeScreenshotAsync());
        OpenSettingsCommand = new RelayCommand(() => RequestOpenSettings?.Invoke());
        RestartServerCommand = new RelayCommand(async () => await RestartServerAsync());
        ToggleServerCommand = new RelayCommand(async () => await ToggleServerAsync());
        OpenScreenshotFolderCommand = new RelayCommand(OpenScreenshotFolder);
        ConfigureFirewallCommand = new RelayCommand(async () => await ConfigureFirewallAsync());

        // ファイアウォール規則の初期確認
        Task.Run(() =>
        {
            var configured = _firewallService.IsFirewallRuleConfigured();
            Application.Current.Dispatcher.Invoke(() => IsFirewallConfigured = configured);
        });

        // エンジンイベント購読
        _engineManager.ServerStarted += OnServerStarted;
        _engineManager.ServerStopped += OnServerStopped;
        _engineManager.ServerError += OnServerError;
        _engineManager.DeviceConnected += OnDeviceConnected;
        _engineManager.ResolutionChanged += OnResolutionChanged;
        _engineManager.DeviceDisconnected += OnDeviceDisconnected;
        _engineManager.VideoWindowReady += OnVideoWindowReady;

        if (_settingsService.CurrentSettings.AutoStartServer)
        {
            _ = _engineManager.StartServerAsync(_settingsService.CurrentSettings);
        }
    }

    private async Task ConfigureFirewallAsync()
    {
        ShowToast("管理者権限の確認ダイアログに応答してください...");
        bool success = await _firewallService.RequestAndConfigureFirewallAsync();
        IsFirewallConfigured = success;

        if (success)
        {
            ShowToast("🛡️ ファイアウォール許可を適用しました！");
            await RestartServerAsync();
        }
        else
        {
            ShowToast("ファイアウォール設定がキャンセルまたは失敗しました");
        }
    }

    private void OnServerStarted(int port)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Status = ConnectionStatus.Listening;
            ShowToast($"AirPlayサーバー開始 (ポート: {port})");
        });
    }

    private void OnServerStopped()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Status = ConnectionStatus.Stopped;
            _currentVideoHwnd = IntPtr.Zero;
            DetachVideoWindowRequested?.Invoke();
        });
    }

    private void OnServerError(string error)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Status = ConnectionStatus.Error;
            ShowToast($"エラー: {error}");
        });
    }

    private void OnDeviceConnected(MirrorSessionInfo session)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            SessionInfo = session;
            Status = ConnectionStatus.Connected;
            ShowToast($"接続されました: {session.DeviceName}");
        });
    }

    private void ToggleOrientation()
    {
        RequestOrientationToggle?.Invoke();
    }

    private void OnResolutionChanged(int width, int height, int fps)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (SessionInfo != null)
            {
                SessionInfo.Width = width;
                SessionInfo.Height = height;
                SessionInfo.Fps = fps;
                OnPropertyChanged(nameof(SessionInfo));
            }
            bool isLandscape = width > height;
            RequestOrientationChange?.Invoke(isLandscape);
            VideoDimensionsChanged?.Invoke(width, height);
        });
    }

    private void OnDeviceDisconnected()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            SessionInfo = null;
            Status = ConnectionStatus.Listening;
            _currentVideoHwnd = IntPtr.Zero;
            DetachVideoWindowRequested?.Invoke();
            ShowToast("iPhoneの接続が終了しました");
        });
    }

    private void OnVideoWindowReady(IntPtr hWnd)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _currentVideoHwnd = hWnd;
            AttachVideoWindowRequested?.Invoke(hWnd);
        });
    }

    public async Task TakeScreenshotAsync()
    {
        var targetHwnd = _currentVideoHwnd;
        if (targetHwnd == IntPtr.Zero)
        {
            // 映像ウィンドウが見つからない場合はメインウィンドウをキャプチャ対象にする
            var mainWin = Application.Current.MainWindow;
            if (mainWin != null)
            {
                targetHwnd = new System.Windows.Interop.WindowInteropHelper(mainWin).Handle;
            }
        }

        var saveDir = _settingsService.CurrentSettings.ScreenshotDirectory;
        var copy = _settingsService.CurrentSettings.CopyScreenshotToClipboard;

        var savedPath = await _captureService.CaptureWindowAsync(targetHwnd, saveDir, copy);
        if (!string.IsNullOrEmpty(savedPath))
        {
            ShowToast($"📸 スクリーンショット保存完了");
        }
        else
        {
            ShowToast("キャプチャに失敗しました");
        }
    }

    public async Task RestartServerAsync()
    {
        // 既存のセッションとBonjour広告を明示的に切断
        _engineManager.StopServer();
        Status = ConnectionStatus.Stopped;
        SessionInfo = null;
        _currentVideoHwnd = IntPtr.Zero;
        DetachVideoWindowRequested?.Invoke();

        // iPhone側への切断パケット到達とポート解放のため少し待機
        await Task.Delay(500);

        await _engineManager.StartServerAsync(_settingsService.CurrentSettings);
        OnPropertyChanged(nameof(ServerName));
        OnPropertyChanged(nameof(Port));
    }

    public async Task ToggleServerAsync()
    {
        if (Status == ConnectionStatus.Stopped)
        {
            await _engineManager.StartServerAsync(_settingsService.CurrentSettings);
        }
        else
        {
            _engineManager.StopServer();
        }
    }

    private void HandleEscape()
    {
        if (IsFullscreen)
        {
            IsFullscreen = false;
        }
        else if (IsCleanView)
        {
            IsCleanView = false;
        }
    }

    private void OpenScreenshotFolder()
    {
        var dir = _settingsService.CurrentSettings.ScreenshotDirectory;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = dir,
            UseShellExecute = true
        });
    }

    public void ShowToast(string message)
    {
        ToastMessage = message;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void UpdateStatusText()
    {
        StatusText = Status switch
        {
            ConnectionStatus.Connected => "接続中",
            ConnectionStatus.Listening => "待機中 (接続可能)",
            ConnectionStatus.Stopped => "停止中",
            ConnectionStatus.Error => "エラー発生",
            _ => "待機中"
        };
    }

    public void Dispose()
    {
        _toastTimer.Stop();
        _engineManager.Dispose();
        GC.SuppressFinalize(this);
    }
}
