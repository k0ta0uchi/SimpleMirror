using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using SimpleMirror.Interop;

namespace SimpleMirror.Services;

public enum VideoScaleMode
{
    Fit,            // 標準フィット (アスペクト比維持で全体表示)
    ZoomLandscape,  // 横画面拡大 (縦フレーム内の横画面ゲームを上下クロップして最大拡大)
    Fill            // 全画面引き伸ばし
}

/// <summary>
/// バックエンド描画ウィンドウ（HWND）をWPF内に安全かつシームレスにホスティングし、
/// 映像コンテンツの縦横向きを自動検知して0msで最大フィット拡大するコントロール
/// </summary>
public class EmbeddedVideoHost : HwndHost
{
    private const string WindowClassName = "SimpleMirror_HostContainer";
    private const int DefaultVideoWidth = 1170;
    private const int DefaultVideoHeight = 2532;
    private const int RequiredConsecutiveMatches = 3;
    private const byte BlackColorThreshold = 24;
    private const int SamplingMarginVertical = 40;
    private const int SamplingMarginHorizontal = 30;
    private static readonly TimeSpan SwitchCooldown = TimeSpan.FromSeconds(2.0);

    private IntPtr _containerHwnd = IntPtr.Zero;
    private IntPtr _childVideoHwnd = IntPtr.Zero;

    private int _videoWidth = DefaultVideoWidth;
    private int _videoHeight = DefaultVideoHeight;
    private bool _keepAspectRatio = true;
    private bool _enableAutoOrientation = false;
    private VideoScaleMode _scaleMode = VideoScaleMode.Fit;

    private readonly DispatcherTimer _syncTimer;
    private readonly DispatcherTimer _autoOrientationTimer;

    private DateTime _lastSwitchTime = DateTime.MinValue;
    private int _consecutiveMatchCount = 0;
    private bool? _pendingOrientation = null;

    public event Action<bool>? AutoOrientationDetected;

    public IntPtr ChildVideoHwnd => _childVideoHwnd;

    public bool EnableAutoOrientation
    {
        get => _enableAutoOrientation;
        set
        {
            _enableAutoOrientation = value;
            if (!value)
            {
                _consecutiveMatchCount = 0;
                _pendingOrientation = null;
            }
        }
    }

    public VideoScaleMode ScaleMode
    {
        get => _scaleMode;
        set
        {
            _scaleMode = value;
            UpdateChildPosition();
        }
    }

    public bool KeepAspectRatio
    {
        get => _keepAspectRatio;
        set
        {
            _keepAspectRatio = value;
            UpdateChildPosition();
        }
    }

    public EmbeddedVideoHost()
    {
        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _syncTimer.Tick += (s, e) => UpdateChildPosition();

        _autoOrientationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _autoOrientationTimer.Tick += (s, e) => CheckContentOrientation();
    }

    public void SetVideoDimensions(int width, int height)
    {
        if (width > 0 && height > 0 && (_videoWidth != width || _videoHeight != height))
        {
            _videoWidth = width;
            _videoHeight = height;
            UpdateChildPosition();
        }
    }

    public void ToggleScaleMode()
    {
        ScaleMode = ScaleMode == VideoScaleMode.Fit ? VideoScaleMode.ZoomLandscape : VideoScaleMode.Fit;
    }

    public void AttachChildWindow(IntPtr childHwnd)
    {
        if (childHwnd == IntPtr.Zero || !NativeMethods.IsWindow(childHwnd))
        {
            return;
        }

        _childVideoHwnd = childHwnd;

        if (_containerHwnd != IntPtr.Zero)
        {
            // まず初期ウィンドウを即座に非表示にして、画面別の位置での一瞬の描画を完全防止
            NativeMethods.ShowWindow(_childVideoHwnd, NativeMethods.SW_HIDE);

            // 枠線・タイトルバー・最大化最小化ボタンを削除して子コントロール化
            var style = (uint)NativeMethods.GetWindowLongPtr(_childVideoHwnd, NativeMethods.GWL_STYLE).ToInt64();
            style &= ~(NativeMethods.WS_POPUP | NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME | 
                       NativeMethods.WS_MINIMIZEBOX | NativeMethods.WS_MAXIMIZEBOX | NativeMethods.WS_SYSMENU);
            style |= NativeMethods.WS_CHILD;
            NativeMethods.SetWindowLongPtr(_childVideoHwnd, NativeMethods.GWL_STYLE, (IntPtr)style);

            // 拡張スタイルからも不要な枠線を削除
            var exStyle = (uint)NativeMethods.GetWindowLongPtr(_childVideoHwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            exStyle &= ~0x00040000u; // WS_EX_APPWINDOW
            NativeMethods.SetWindowLongPtr(_childVideoHwnd, NativeMethods.GWL_EXSTYLE, (IntPtr)exStyle);

            NativeMethods.SetParent(_childVideoHwnd, _containerHwnd);
            UpdateChildPosition();

            // 正しいコンテナ位置に配置された後で表示
            NativeMethods.ShowWindow(_childVideoHwnd, NativeMethods.SW_SHOW);

            // 描画パイプライン同期および自動向き検知タイマーを開始
            _syncTimer.Start();
            _autoOrientationTimer.Start();
            Task.Delay(2500).ContinueWith(_ => Application.Current.Dispatcher.Invoke(() => _syncTimer.Stop()));
        }
    }

    public void DetachChildWindow()
    {
        _syncTimer.Stop();
        _autoOrientationTimer.Stop();
        _consecutiveMatchCount = 0;
        _pendingOrientation = null;

        if (_childVideoHwnd != IntPtr.Zero && NativeMethods.IsWindow(_childVideoHwnd))
        {
            NativeMethods.SetParent(_childVideoHwnd, IntPtr.Zero);
            _childVideoHwnd = IntPtr.Zero;
        }
    }

    private void CheckContentOrientation()
    {
        if (!_enableAutoOrientation)
        {
            return;
        }

        if (_childVideoHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_childVideoHwnd) || _containerHwnd == IntPtr.Zero)
        {
            return;
        }

        // クールダウンガード: 切り替え後2秒間は自動切り替えを抑止（往復チャタリングを完全防止）
        if (DateTime.Now - _lastSwitchTime < SwitchCooldown)
        {
            return;
        }

        NativeMethods.GetWindowRect(_containerHwnd, out var rc);
        if (rc.Width < 50 || rc.Height < 50)
        {
            return;
        }

        IntPtr hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero)
        {
            return;
        }

        try
        {
            bool? detectedState = DetermineOrientationFromScreen(hdcScreen, rc);

            if (detectedState.HasValue && detectedState.Value != (ScaleMode == VideoScaleMode.ZoomLandscape))
            {
                if (_pendingOrientation == detectedState.Value)
                {
                    _consecutiveMatchCount++;
                    // 3回連続一致（約750msデバウンス）で切り替え確定
                    if (_consecutiveMatchCount >= RequiredConsecutiveMatches)
                    {
                        _lastSwitchTime = DateTime.Now;
                        _consecutiveMatchCount = 0;
                        _pendingOrientation = null;
                        AutoOrientationDetected?.Invoke(detectedState.Value);
                    }
                }
                else
                {
                    _pendingOrientation = detectedState.Value;
                    _consecutiveMatchCount = 1;
                }
            }
            else
            {
                _consecutiveMatchCount = 0;
                _pendingOrientation = null;
            }
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    private bool? DetermineOrientationFromScreen(IntPtr hdcScreen, NativeMethods.RECT rc)
    {
        int cx = rc.Left + rc.Width / 2;
        int cy = rc.Top + rc.Height / 2;

        uint pCenter = NativeMethods.GetPixel(hdcScreen, cx, cy);
        bool centerIsBlack = IsBlackColor(pCenter);

        if (ScaleMode == VideoScaleMode.Fit)
        {
            // 縦画面（Fit）表示中: コンテナ上下（内側40px）に黒帯が発生したか判定
            uint pTop = NativeMethods.GetPixel(hdcScreen, cx, rc.Top + Math.Min(SamplingMarginVertical, rc.Height / 4));
            uint pBottom = NativeMethods.GetPixel(hdcScreen, cx, rc.Bottom - Math.Min(SamplingMarginVertical, rc.Height / 4));

            if (IsBlackColor(pTop) && IsBlackColor(pBottom) && !centerIsBlack)
            {
                return true; // 横画面コンテンツ検知
            }
        }
        else if (ScaleMode == VideoScaleMode.ZoomLandscape)
        {
            // 横画面（ZoomLandscape）表示中: コンテナ左右（内側30px）に黒帯が発生したか判定
            uint pLeft = NativeMethods.GetPixel(hdcScreen, rc.Left + Math.Min(SamplingMarginHorizontal, rc.Width / 8), cy);
            uint pRight = NativeMethods.GetPixel(hdcScreen, rc.Right - Math.Min(SamplingMarginHorizontal, rc.Width / 8), cy);

            if (IsBlackColor(pLeft) && IsBlackColor(pRight) && !centerIsBlack)
            {
                return false; // 縦画面コンテンツへ復帰検知
            }
        }

        return null;
    }

    private static bool IsBlackColor(uint color)
    {
        if (color == 0xFFFFFFFF) return false;
        byte r = (byte)(color & 0xFF);
        byte g = (byte)((color >> 8) & 0xFF);
        byte b = (byte)((color >> 16) & 0xFF);
        return r < BlackColorThreshold && g < BlackColorThreshold && b < BlackColorThreshold;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _containerHwnd = CreateWindowContainer(hwndParent.Handle);

        if (_childVideoHwnd != IntPtr.Zero && NativeMethods.IsWindow(_childVideoHwnd))
        {
            AttachChildWindow(_childVideoHwnd);
        }

        return new HandleRef(this, _containerHwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DetachChildWindow();
        if (hwnd.Handle != IntPtr.Zero && NativeMethods.IsWindow(hwnd.Handle))
        {
            NativeMethods.SendMessage(hwnd.Handle, NativeMethods.WM_DESTROY, IntPtr.Zero, IntPtr.Zero);
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateChildPosition();
    }

    public void UpdateChildPosition()
    {
        if (_childVideoHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_childVideoHwnd) || _containerHwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.GetClientRect(_containerHwnd, out var containerRect);
        int containerW = containerRect.Width;
        int containerH = containerRect.Height;

        if (containerW <= 0 || containerH <= 0)
        {
            return;
        }

        CalculateTargetViewport(containerW, containerH, out int targetX, out int targetY, out int targetW, out int targetH);

        // 親動画ウィンドウの位置とサイズを更新
        NativeMethods.SetWindowPos(
            _childVideoHwnd,
            IntPtr.Zero,
            targetX,
            targetY,
            targetW,
            targetH,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW);

        NativeMethods.MoveWindow(_childVideoHwnd, targetX, targetY, targetW, targetH, true);

        // 内部の Direct3D / GStreamer 子ウィンドウも同期リサイズ
        NativeMethods.EnumChildWindows(_childVideoHwnd, (childHwnd, lParam) =>
        {
            if (NativeMethods.IsWindow(childHwnd))
            {
                NativeMethods.MoveWindow(childHwnd, 0, 0, targetW, targetH, true);
                NativeMethods.SetWindowPos(
                    childHwnd,
                    IntPtr.Zero,
                    0,
                    0,
                    targetW,
                    targetH,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW);
            }
            return true;
        }, IntPtr.Zero);
    }

    private void CalculateTargetViewport(int containerW, int containerH, out int targetX, out int targetY, out int targetW, out int targetH)
    {
        targetX = 0;
        targetY = 0;
        targetW = containerW;
        targetH = containerH;

        if (_scaleMode == VideoScaleMode.ZoomLandscape && _videoWidth > 0 && _videoHeight > 0)
        {
            // 横画面拡大モード: 縦フレーム内の横画面ゲーム部分をコンテナ幅・高さいっぱいに最大ズーム
            if (_videoWidth <= _videoHeight)
            {
                targetW = containerW;
                targetH = (int)Math.Round(containerW * ((double)_videoHeight / _videoWidth));
                targetX = 0;
                targetY = (containerH - targetH) / 2;
            }
            else
            {
                double aspectVideo = (double)_videoWidth / _videoHeight;
                double aspectContainer = (double)containerW / containerH;
                if (aspectContainer > aspectVideo)
                {
                    targetH = containerH;
                    targetW = (int)Math.Round(containerH * aspectVideo);
                    targetX = (containerW - targetW) / 2;
                    targetY = 0;
                }
                else
                {
                    targetW = containerW;
                    targetH = (int)Math.Round(containerW / aspectVideo);
                    targetX = 0;
                    targetY = (containerH - targetH) / 2;
                }
            }
        }
        else if (_keepAspectRatio && _videoWidth > 0 && _videoHeight > 0)
        {
            // 標準フィットモード (レターボックス/ピラーボックス)
            double aspectVideo = (double)_videoWidth / _videoHeight;
            double aspectContainer = (double)containerW / containerH;

            if (aspectContainer > aspectVideo)
            {
                targetH = containerH;
                targetW = Math.Max(1, (int)Math.Round(containerH * aspectVideo));
                targetX = (containerW - targetW) / 2;
                targetY = 0;
            }
            else
            {
                targetW = containerW;
                targetH = Math.Max(1, (int)Math.Round(containerW / aspectVideo));
                targetX = 0;
                targetY = (containerH - targetH) / 2;
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
        [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    private static IntPtr CreateWindowContainer(IntPtr parentHwnd)
    {
        const int WS_CHILD = 0x40000000;
        const int WS_VISIBLE = 0x10000000;
        const int WS_CLIPCHILDREN = 0x02000000;

        return CreateWindowEx(
            0,
            "STATIC",
            "",
            WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
            0, 0, 100, 100,
            parentHwnd,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
    }
}
