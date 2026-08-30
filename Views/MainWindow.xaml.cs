using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using SimpleMirror.Interop;
using SimpleMirror.Models;
using SimpleMirror.Services;
using SimpleMirror.ViewModels;

namespace SimpleMirror.Views;

/// <summary>
/// MainWindow.xaml の相互作用ロジック
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly SettingsService _settingsService;
    private EmbeddedVideoHost? _videoHost;
    private WindowState _previousWindowState = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();

        _settingsService = new SettingsService();
        var engineManager = new AirPlayEngineManager();
        var captureService = new CaptureService();
        var firewallService = new FirewallService();

        _viewModel = new MainViewModel(_settingsService, engineManager, captureService, firewallService);
        DataContext = _viewModel;

        // デフォルトでは縦画面で起動
        Width = 480;
        Height = 840;

        // イベントバインド
        _viewModel.RequestOpenSettings += OpenSettingsDialog;
        _viewModel.AttachVideoWindowRequested += AttachVideoWindow;
        _viewModel.DetachVideoWindowRequested += DetachVideoWindow;
        _viewModel.VideoDimensionsChanged += OnVideoDimensionsChanged;
        _viewModel.RequestOrientationChange += OnAutoOrientationChanged;
        _viewModel.RequestOrientationToggle += OnManualOrientationToggle;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 待機中アニメーション開始
        if (Resources["RadarPulseAnimation"] is Storyboard sb)
        {
            sb.Begin(this, true);
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 設定保存
        if (WindowState == WindowState.Normal)
        {
            _settingsService.CurrentSettings.WindowWidth = Width;
            _settingsService.CurrentSettings.WindowHeight = Height;
            _settingsService.Save();
        }

        _viewModel.Dispose();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsFullscreen))
        {
            UpdateFullscreenMode(_viewModel.IsFullscreen);
        }
        else if (e.PropertyName == nameof(MainViewModel.IsCleanView))
        {
            UpdateCleanViewMode(_viewModel.IsCleanView);
        }
        else if (e.PropertyName == nameof(MainViewModel.EnableAutoOrientation))
        {
            if (_videoHost != null)
            {
                _videoHost.EnableAutoOrientation = _viewModel.EnableAutoOrientation;
            }
        }
    }

    private void UpdateFullscreenMode(bool fullscreen)
    {
        if (fullscreen)
        {
            _previousWindowState = WindowState;

            // GUIコントロールを完全非表示化
            TitleBarElement.Visibility = Visibility.Collapsed;
            ControlBarElement.Visibility = Visibility.Collapsed;
            StatusBarElement.Visibility = Visibility.Collapsed;

            // ウィンドウ装飾・影・角丸を解除して完全ボーダーレス化
            MainWindowBorder.CornerRadius = new CornerRadius(0);
            MainWindowBorder.BorderThickness = new Thickness(0);
            MainWindowBorder.Effect = null;
            Background = System.Windows.Media.Brushes.Black;

            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            WindowState = WindowState.Maximized;
        }
        else
        {
            // GUIコントロールを再表示
            TitleBarElement.Visibility = Visibility.Visible;
            ControlBarElement.Visibility = Visibility.Visible;
            StatusBarElement.Visibility = Visibility.Visible;

            // ウィンドウ装飾・影・角丸を復元
            MainWindowBorder.CornerRadius = new CornerRadius(14);
            MainWindowBorder.BorderThickness = new Thickness(1);
            if (FindResource("WindowDropShadow") is System.Windows.Media.Effects.DropShadowEffect dropShadow)
            {
                MainWindowBorder.Effect = dropShadow;
            }
            Background = System.Windows.Media.Brushes.Transparent;

            WindowState = _previousWindowState == WindowState.Maximized ? WindowState.Normal : _previousWindowState;
        }

        // 同期レイアウト更新と子ウィンドウ同期で一瞬の画面ズレを完全解消
        UpdateLayout();
        _videoHost?.UpdateChildPosition();
    }

    private void UpdateCleanViewMode(bool cleanView)
    {
        if (cleanView)
        {
            // GUIコントロールを完全非表示（OBSウィンドウキャプチャで映像のみ取得可能）
            TitleBarElement.Visibility = Visibility.Collapsed;
            ControlBarElement.Visibility = Visibility.Collapsed;
            StatusBarElement.Visibility = Visibility.Collapsed;

            // ウィンドウ装飾・影・角丸を解除
            MainWindowBorder.CornerRadius = new CornerRadius(0);
            MainWindowBorder.BorderThickness = new Thickness(0);
            MainWindowBorder.Effect = null;
            Background = System.Windows.Media.Brushes.Black;

            _viewModel.ShowToast("🎬 OBSクリーンモード有効（F10でGUI復帰）");
        }
        else
        {
            if (!_viewModel.IsFullscreen)
            {
                // GUIコントロールを再表示
                TitleBarElement.Visibility = Visibility.Visible;
                ControlBarElement.Visibility = Visibility.Visible;
                StatusBarElement.Visibility = Visibility.Visible;

                // ウィンドウ装飾・影・角丸を復元
                MainWindowBorder.CornerRadius = new CornerRadius(14);
                MainWindowBorder.BorderThickness = new Thickness(1);
                if (FindResource("WindowDropShadow") is System.Windows.Media.Effects.DropShadowEffect dropShadow)
                {
                    MainWindowBorder.Effect = dropShadow;
                }
                Background = System.Windows.Media.Brushes.Transparent;
            }
        }

        // 同期レイアウト更新と子ウィンドウ同期で一瞬の画面ズレを完全解消
        UpdateLayout();
        _videoHost?.UpdateChildPosition();
    }

    private void ExitCleanView_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _viewModel.IsCleanView = false;
    }

    private void ExitFullscreen_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _viewModel.IsFullscreen = false;
    }

    private void VideoContainer_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _viewModel.ToggleFullscreenCommand.Execute(null);
        }
    }

    private void AttachVideoWindow(IntPtr hWnd)
    {
        if (_videoHost == null)
        {
            _videoHost = new EmbeddedVideoHost
            {
                KeepAspectRatio = _viewModel.KeepAspectRatio,
                EnableAutoOrientation = _viewModel.EnableAutoOrientation
            };
            _videoHost.AutoOrientationDetected += OnAutoOrientationChanged;
            VideoHostContainer.Content = _videoHost;
        }

        _videoHost.AttachChildWindow(hWnd);
    }

    private void DetachVideoWindow()
    {
        _videoHost?.DetachChildWindow();
    }

    private void OnVideoDimensionsChanged(int width, int height)
    {
        _videoHost?.SetVideoDimensions(width, height);
    }

    private void OnAutoOrientationChanged(bool isLandscape)
    {
        Dispatcher.Invoke(() =>
        {
            if (_videoHost != null)
            {
                _videoHost.ScaleMode = isLandscape ? VideoScaleMode.ZoomLandscape : VideoScaleMode.Fit;
            }

            if (WindowState != WindowState.Maximized)
            {
                bool currentIsLandscape = Width > Height;
                if (currentIsLandscape != isLandscape)
                {
                    // 中心位置を維持してスムーズにリサイズ
                    if (isLandscape)
                    {
                        ResizeWindowPreservingCenter(860, 520);
                    }
                    else
                    {
                        ResizeWindowPreservingCenter(480, 840);
                    }
                }
            }

            _videoHost?.UpdateChildPosition();
        });
    }

    private void OnManualOrientationToggle()
    {
        if (_videoHost != null)
        {
            _videoHost.ToggleScaleMode();
            bool isLandscapeZoom = _videoHost.ScaleMode == VideoScaleMode.ZoomLandscape;

            if (WindowState != WindowState.Maximized)
            {
                // 中心位置を維持してスムーズにリサイズ
                if (isLandscapeZoom)
                {
                    ResizeWindowPreservingCenter(860, 520);
                }
                else
                {
                    ResizeWindowPreservingCenter(480, 840);
                }
            }

            _viewModel.ShowToast(isLandscapeZoom 
                ? "🔄 横画面フィット拡大モード（黒帯排除）" 
                : "🔄 標準縦画面モード");

            _videoHost.UpdateChildPosition();
        }
    }

    private void ResizeWindowPreservingCenter(double targetW, double targetH)
    {
        if (WindowState == WindowState.Maximized) return;

        double centerX = Left + Width / 2.0;
        double centerY = Top + Height / 2.0;

        Width = targetW;
        Height = targetH;

        Left = centerX - targetW / 2.0;
        Top = centerY - targetH / 2.0;

        EnsureWindowWithinScreenBounds();
    }

    private void EnsureWindowWithinScreenBounds()
    {
        var helper = new WindowInteropHelper(this);
        if (helper.Handle != IntPtr.Zero)
        {
            IntPtr hMonitor = NativeMethods.MonitorFromWindow(helper.Handle, 2); // MONITOR_DEFAULTTONEAREST = 2
            if (hMonitor != IntPtr.Zero)
            {
                var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
                if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                {
                    var work = mi.rcWork;
                    var source = PresentationSource.FromVisual(this);
                    double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                    double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                    double workLeft = work.Left / dpiX;
                    double workTop = work.Top / dpiY;
                    double workRight = work.Right / dpiX;
                    double workBottom = work.Bottom / dpiY;

                    if (Left + Width > workRight) Left = Math.Max(workLeft, workRight - Width);
                    if (Top + Height > workBottom) Top = Math.Max(workTop, workBottom - Height);
                    if (Left < workLeft) Left = workLeft;
                    if (Top < workTop) Top = workTop;
                    return;
                }
            }
        }

        // フォールバック（プライマリモニター）
        var screenW = SystemParameters.WorkArea.Width;
        var screenH = SystemParameters.WorkArea.Height;
        if (Left + Width > screenW) Left = Math.Max(0, screenW - Width);
        if (Top + Height > screenH) Top = Math.Max(0, screenH - Height);
        if (Left < 0) Left = 0;
        if (Top < 0) Top = 0;
    }

    private async void OpenSettingsDialog()
    {
        var settingsVm = new SettingsViewModel(_settingsService);
        var settingsWin = new SettingsWindow(settingsVm)
        {
            Owner = this
        };

        if (settingsWin.ShowDialog() == true)
        {
            var profile = _settingsService.CurrentSettings.Profile;
            string profileName = profile switch
            {
                PerformanceProfile.Performance => "⚡ 超低遅延 / パフォーマンス",
                PerformanceProfile.Quality => "💎 高画質 / クオリティ",
                _ => "⚖️ 標準 / バランス"
            };

            _viewModel.ShowToast($"⚙️ 設定を保存し「{profileName}」を適用しました");
            await _viewModel.RestartServerAsync();
        }
    }

    private void MainWindow_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _viewModel.IsCleanView)
        {
            DragMove();
        }
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
            }
            else
            {
                DragMove();
            }
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            MainWindowBorder.CornerRadius = new CornerRadius(14);
            MainWindowBorder.BorderThickness = new Thickness(1);
        }
        else
        {
            WindowState = WindowState.Maximized;
            MainWindowBorder.CornerRadius = new CornerRadius(0);
            MainWindowBorder.BorderThickness = new Thickness(0);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
