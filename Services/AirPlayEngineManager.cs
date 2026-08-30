using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using SimpleMirror.Interop;
using SimpleMirror.Models;

namespace SimpleMirror.Services;

/// <summary>
/// AirPlayバックエンドプロセスのライフサイクルおよびイベント管理
/// </summary>
public partial class AirPlayEngineManager : IDisposable
{
    private const int DefaultPort = 7000;
    private const int DefaultWidth = 1170;
    private const int DefaultHeight = 2532;
    private const int DefaultFps = 60;
    private const int AF_INET = 2;
    private const uint MIB_TCP_STATE_LISTEN = 2;
    private const int MaxPortScanRetries = 15;
    private const int PortScanIntervalMs = 200;

    private static readonly Regex ConnectionRegex = new(
        @"(?:Connection (?:accepted|from)|client connected).*?(?:from\s+)?(?<ip>\d{1,3}(?:\.\d{1,3}){3})?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DeviceNameRegex = new(
        @"(?:Device|Client)\s+name:\s*(?<name>.+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ResolutionRegex = new(
        @"(?<w>\d{3,4})\s*[x×]\s*(?<h>\d{3,4})(?:.*?@\s*(?<fps>\d{2,3})\s*fps)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CleanNameRegex = new(
        @"[^\w\-]+",
        RegexOptions.Compiled);

    private Process? _serverProcess;
    private bool _isDisposed;
    private readonly System.Timers.Timer _windowSearchTimer;
    private readonly BonjourAirPlayRegistrar _bonjourRegistrar;

    public event Action<int>? ServerStarted;
    public event Action<string>? ServerError;
    public event Action? ServerStopped;
    public event Action<MirrorSessionInfo>? DeviceConnected;
    public event Action<int, int, int>? ResolutionChanged;
    public event Action? DeviceDisconnected;
    public event Action<IntPtr>? VideoWindowReady;

    public ConnectionStatus CurrentStatus { get; private set; } = ConnectionStatus.Stopped;
    public MirrorSessionInfo? CurrentSession { get; private set; }

    public string ActiveIpAddress => _bonjourRegistrar.ActiveIpAddress;
    public bool IsBonjourAvailable => _bonjourRegistrar.IsBonjourAvailable;

    public AirPlayEngineManager()
    {
        _bonjourRegistrar = new BonjourAirPlayRegistrar();
        _windowSearchTimer = new System.Timers.Timer(500);
        _windowSearchTimer.Elapsed += (s, e) => CheckForRendererWindow();
    }

    public async Task<bool> StartServerAsync(AppSettings settings)
    {
        return await Task.Run(() =>
        {
            StopServer();

            var enginePath = FindEngineExecutable();
            if (string.IsNullOrEmpty(enginePath) || !File.Exists(enginePath))
            {
                // バックエンド未配置時はフォールバックとしてBonjour登録
                _bonjourRegistrar.RegisterServices(settings.ServerName, settings.Port);
                CurrentStatus = ConnectionStatus.Listening;
                ServerStarted?.Invoke(settings.Port);
                return true;
            }

            try
            {
                var engineDir = Path.GetDirectoryName(enginePath) ?? AppDomain.CurrentDomain.BaseDirectory;

                GenerateArgumentsConfigFile(settings);

                var startInfo = CreateProcessStartInfo(enginePath, engineDir);

                _serverProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                _serverProcess.OutputDataReceived += OnOutputDataReceived;
                _serverProcess.ErrorDataReceived += OnErrorDataReceived;
                _serverProcess.Exited += (s, e) =>
                {
                    CurrentStatus = ConnectionStatus.Stopped;
                    ServerStopped?.Invoke();
                };

                if (_serverProcess.Start())
                {
                    _serverProcess.BeginOutputReadLine();
                    _serverProcess.BeginErrorReadLine();
                    _windowSearchTimer.Start();

                    // プロセスがリッスンを開始した実際の動的ポートを検出
                    int activePort = WaitForActivePort(_serverProcess.Id, settings.Port);

                    // 検出された実際のポート番号で Bonjour / mDNS 告知を即座に登録
                    _bonjourRegistrar.RegisterServices(settings.ServerName, activePort);

                    CurrentStatus = ConnectionStatus.Listening;
                    ServerStarted?.Invoke(activePort);
                    return true;
                }
            }
            catch (Exception ex)
            {
                ServerError?.Invoke($"Failed to start engine: {ex.Message}");
            }

            return false;
        });
    }

    private static void GenerateArgumentsConfigFile(AppSettings settings)
    {
        var configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "leapbtw", "uxplay-windows");
        if (!Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }

        var cleanServerName = CleanNameRegex.Replace(settings.ServerName, "-").Trim('-');
        if (string.IsNullOrEmpty(cleanServerName)) cleanServerName = "SimpleMirror";
        var configPath = Path.Combine(configDir, "arguments.txt");

        var profileArgs = settings.Profile switch
        {
            PerformanceProfile.Performance => "-fps 60 -vsync no",
            PerformanceProfile.Quality => "-fps 60 -s 2560x1440",
            _ => "-fps 60"
        };

        var rotationArg = settings.RotationDegrees switch
        {
            90 => "-r R",
            270 => "-r L",
            180 => "-f I",
            _ => ""
        };

        var argsLine = $"-n {cleanServerName} -nh {profileArgs} {rotationArg}".Trim();
        File.WriteAllText(configPath, argsLine);
    }

    private static ProcessStartInfo CreateProcessStartInfo(string enginePath, string engineDir)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = enginePath,
            WorkingDirectory = engineDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // GStreamer およびランタイム DLL パスを PATH 環境変数に追加
        var gstPath = Path.Combine(engineDir, "lib", "gstreamer-1.0");
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        startInfo.EnvironmentVariables["PATH"] = $"{engineDir};{gstPath};{currentPath}";

        return startInfo;
    }

    private int WaitForActivePort(int processId, int fallbackPort)
    {
        int activePort = 0;
        for (int i = 0; i < MaxPortScanRetries; i++)
        {
            Thread.Sleep(PortScanIntervalMs);
            if (_serverProcess?.HasExited ?? true) break;
            activePort = DetectListeningPort(processId);
            if (activePort > 0) break;
        }

        return activePort > 0 ? activePort : fallbackPort;
    }

    public void StopServer()
    {
        _windowSearchTimer.Stop();
        _bonjourRegistrar.UnregisterServices();

        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            try
            {
                _serverProcess.Kill(true);
                _serverProcess.WaitForExit(1000);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AirPlayEngineManager] Stop error: {ex.Message}");
            }
            finally
            {
                _serverProcess.Dispose();
                _serverProcess = null;
            }
        }

        CurrentStatus = ConnectionStatus.Stopped;
        CurrentSession = null;
        ServerStopped?.Invoke();
    }

    private static string? FindEngineExecutable()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "Engine", "uxplay-windows.exe"),
            Path.Combine(baseDir, "uxplay-windows.exe"),
            Path.Combine(baseDir, "Engine", "uxplay.exe"),
            Path.Combine(baseDir, "uxplay.exe"),
            Path.Combine(@"C:\Workspace\SimpleMirror\Engine", "uxplay-windows.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data)) return;
        ProcessLogLine(e.Data);
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data)) return;
        ProcessLogLine(e.Data);
    }

    private void ProcessLogLine(string line)
    {
        Debug.WriteLine($"[AirPlay Engine] {line}");

        TryDetectConnection(line);
        TryDetectDeviceName(line);
        TryDetectResolution(line);
        TryDetectDisconnection(line);
    }

    private void TryDetectConnection(string line)
    {
        var match = ConnectionRegex.Match(line);
        if (match.Success && CurrentStatus != ConnectionStatus.Connected)
        {
            CurrentStatus = ConnectionStatus.Connected;
            CurrentSession = new MirrorSessionInfo
            {
                ClientIp = match.Groups["ip"].Value,
                DeviceName = "iPhone",
                ConnectedAt = DateTime.Now
            };
            DeviceConnected?.Invoke(CurrentSession);
        }
    }

    private void TryDetectDeviceName(string line)
    {
        var match = DeviceNameRegex.Match(line);
        if (match.Success && CurrentSession != null)
        {
            CurrentSession.DeviceName = match.Groups["name"].Value.Trim();
        }
    }

    private void TryDetectResolution(string line)
    {
        var match = ResolutionRegex.Match(line);
        if (!match.Success) return;

        if (int.TryParse(match.Groups["w"].Value, out int w) &&
            int.TryParse(match.Groups["h"].Value, out int h))
        {
            int fps = DefaultFps;
            if (match.Groups["fps"].Success)
            {
                int.TryParse(match.Groups["fps"].Value, out fps);
            }

            if (CurrentSession != null)
            {
                CurrentSession.Width = w;
                CurrentSession.Height = h;
                CurrentSession.Fps = fps;
            }

            ResolutionChanged?.Invoke(w, h, fps);
        }
    }

    private void TryDetectDisconnection(string line)
    {
        if (line.Contains("disconnected", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Connection closed", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("teardown", StringComparison.OrdinalIgnoreCase))
        {
            CurrentStatus = ConnectionStatus.Listening;
            CurrentSession = null;
            DeviceDisconnected?.Invoke();
        }
    }

    private void CheckForRendererWindow()
    {
        if (_serverProcess == null || _serverProcess.HasExited) return;

        uint procId = (uint)_serverProcess.Id;
        IntPtr foundHwnd = IntPtr.Zero;

        NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint wndProcId);
            if (wndProcId == procId && NativeMethods.IsWindowVisible(hWnd))
            {
                var sbClass = new StringBuilder(256);
                NativeMethods.GetClassName(hWnd, sbClass, sbClass.Capacity);
                string className = sbClass.ToString();

                var sbTitle = new StringBuilder(256);
                NativeMethods.GetWindowText(hWnd, sbTitle, sbTitle.Capacity);
                string title = sbTitle.ToString();

                // Direct3D / GStreamer / SDL / Qt 描画ウィンドウを判定
                if (!className.Contains("Console") && 
                    !className.Contains("Message") && 
                    !className.Contains("Tray") &&
                    (title.Contains("AirPlay", StringComparison.OrdinalIgnoreCase) ||
                     title.Contains("UxPlay", StringComparison.OrdinalIgnoreCase) ||
                     title.Contains("SimpleMirror", StringComparison.OrdinalIgnoreCase) ||
                     className.Contains("D3D", StringComparison.OrdinalIgnoreCase) ||
                     className.Contains("Gst", StringComparison.OrdinalIgnoreCase) ||
                     className.Contains("Qt", StringComparison.OrdinalIgnoreCase)))
                {
                    NativeMethods.GetClientRect(hWnd, out var rc);
                    if (rc.Width > 50 && rc.Height > 50)
                    {
                        foundHwnd = hWnd;
                        return false; // 探索終了
                    }
                }
            }
            return true;
        }, IntPtr.Zero);

        if (foundHwnd != IntPtr.Zero)
        {
            if (CurrentStatus != ConnectionStatus.Connected)
            {
                CurrentStatus = ConnectionStatus.Connected;
                CurrentSession ??= new MirrorSessionInfo
                {
                    DeviceName = "iPhone",
                    ConnectedAt = DateTime.Now,
                    Width = DefaultWidth,
                    Height = DefaultHeight
                };
                DeviceConnected?.Invoke(CurrentSession);
            }

            VideoWindowReady?.Invoke(foundHwnd);
        }
    }

    private static int DetectListeningPort(int processId)
    {
        try
        {
            int bufferSize = 0;
            NativeMethods.GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, NativeMethods.TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);

            if (bufferSize > 0)
            {
                IntPtr tcpTablePtr = Marshal.AllocHGlobal(bufferSize);
                try
                {
                    uint ret = NativeMethods.GetExtendedTcpTable(tcpTablePtr, ref bufferSize, true, AF_INET, NativeMethods.TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
                    if (ret == 0)
                    {
                        int entryCount = Marshal.ReadInt32(tcpTablePtr);
                        IntPtr rowPtr = IntPtr.Add(tcpTablePtr, 4);

                        for (int i = 0; i < entryCount; i++)
                        {
                            var row = Marshal.PtrToStructure<NativeMethods.MIB_TCPROW_OWNER_PID>(rowPtr);
                            if (row.owningPid == (uint)processId && row.state == MIB_TCP_STATE_LISTEN)
                            {
                                int port = row.LocalPort;
                                if (port > 0)
                                {
                                    return port;
                                }
                            }
                            rowPtr = IntPtr.Add(rowPtr, Marshal.SizeOf<NativeMethods.MIB_TCPROW_OWNER_PID>());
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(tcpTablePtr);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AirPlayEngineManager] Detect port error: {ex.Message}");
        }

        return 0;
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            StopServer();
            _bonjourRegistrar.Dispose();
            _windowSearchTimer.Dispose();
            _isDisposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
