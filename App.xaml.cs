using System.Diagnostics;
using System.Windows;

namespace SimpleMirror;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 過去の異常終了等で残った孤立プロセスを確実にクリーンアップ
        CleanupOrphanProcesses();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CleanupOrphanProcesses();
        base.OnExit(e);
    }

    private static void CleanupOrphanProcesses()
    {
        try
        {
            var currentPid = Process.GetCurrentProcess().Id;

            // 他の古いSimpleMirrorインスタンスを終了
            foreach (var p in Process.GetProcessesByName("SimpleMirror"))
            {
                if (p.Id != currentPid)
                {
                    try { p.Kill(); p.WaitForExit(500); } catch { }
                }
            }

            // 孤立したuxplay-windowsプロセスを終了
            foreach (var p in Process.GetProcessesByName("uxplay-windows"))
            {
                try { p.Kill(); p.WaitForExit(500); } catch { }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Cleanup error: {ex.Message}");
        }
    }
}
