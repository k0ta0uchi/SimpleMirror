using System.Diagnostics;
using System.IO;

namespace SimpleMirror.Services;

/// <summary>
/// Windows Defender ファイアウォールの状態確認および UAC 昇格による自動許可登録サービス
/// </summary>
public class FirewallService
{
    private const string RuleName = "SimpleMirror_AirPlay";

    /// <summary>
    /// ファイアウォール規則が既に登録されているか確認
    /// </summary>
    public bool IsFirewallRuleConfigured()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall show rule name=\"{RuleName}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(2000);
                return output.Contains(RuleName, StringComparison.OrdinalIgnoreCase) &&
                       !output.Contains("指定された条件に一致する規則はありません", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FirewallService] Check rule error: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// UAC昇格ダイアログを表示して、自動でファイアウォール受信規則を登録する
    /// </summary>
    public async Task<bool> RequestAndConfigureFirewallAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var exePath = Environment.ProcessPath ?? 
                              Process.GetCurrentProcess().MainModule?.FileName ?? 
                              Path.Combine(baseDir, "SimpleMirror.exe");
                var engineExePath = Path.Combine(baseDir, "Engine", "uxplay-windows.exe");
                var mdnsExePath = Path.Combine(baseDir, "Engine", "mDNSResponder.exe");

                // netshコマンドでアプリ本体、UxPlayエンジン、mDNSの受信を全プロファイルで許可
                var commands = new[]
                {
                    $"netsh advfirewall firewall delete rule name=\"{RuleName}\"",
                    $"netsh advfirewall firewall delete rule name=\"{RuleName}_Engine\"",
                    $"netsh advfirewall firewall delete rule name=\"{RuleName}_mDNS\"",
                    $"netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{exePath}\" enable=yes profile=any",
                    $"netsh advfirewall firewall add rule name=\"{RuleName}_Engine\" dir=in action=allow program=\"{engineExePath}\" enable=yes profile=any",
                    $"netsh advfirewall firewall add rule name=\"{RuleName}_mDNS\" dir=in action=allow program=\"{mdnsExePath}\" enable=yes profile=any",
                    $"netsh advfirewall firewall add rule name=\"{RuleName}_TCP\" dir=in action=allow protocol=TCP localport=7000 enable=yes profile=any",
                    $"netsh advfirewall firewall add rule name=\"{RuleName}_UDP\" dir=in action=allow protocol=UDP localport=5353 enable=yes profile=any"
                };

                var script = string.Join(" ; ", commands);

                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"",
                    UseShellExecute = true,
                    Verb = "runas" // Windows UAC 昇格ダイアログを表示
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    process.WaitForExit(5000);
                    return IsFirewallRuleConfigured();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FirewallService] Request UAC error (User might have cancelled): {ex.Message}");
            }

            return false;
        });
    }
}
