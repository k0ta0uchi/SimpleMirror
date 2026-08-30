namespace SimpleMirror.Models;

/// <summary>
/// 画質と遅延（レイテンシ）の動作プロファイル
/// </summary>
public enum PerformanceProfile
{
    /// <summary>
    /// パフォーマンス優先（超低遅延: 720p@60fps, 垂直同期バッファスキップ）
    /// </summary>
    Performance,

    /// <summary>
    /// バランス（標準: 1080p@60fps, 通常バッファ）
    /// </summary>
    Balanced,

    /// <summary>
    /// クオリティ優先（高精細: 1440p/2K@60fps, 高品質カラー補間）
    /// </summary>
    Quality
}

/// <summary>
/// アプリケーション設定
/// </summary>
public class AppSettings
{
    /// <summary>
    /// 動作プロファイル（遅延・画質の最適化設定）
    /// </summary>
    public PerformanceProfile Profile { get; set; } = PerformanceProfile.Balanced;

    /// <summary>
    /// iPhoneに表示されるAirPlayレシーバー名
    /// </summary>
    public string ServerName { get; set; } = $"SimpleMirror-{Environment.MachineName}";

    /// <summary>
    /// ポート番号（デフォルト: 7000）
    /// </summary>
    public int Port { get; set; } = 7000;

    /// <summary>
    /// 常に最前面表示するかどうか
    /// </summary>
    public bool AlwaysOnTop { get; set; } = false;

    /// <summary>
    /// アスペクト比を固定するかどうか
    /// </summary>
    public bool KeepAspectRatio { get; set; } = true;

    /// <summary>
    /// 音声再生を有効化するかどうか
    /// </summary>
    public bool EnableAudio { get; set; } = true;

    /// <summary>
    /// iPhoneの向きに合わせた自動回転を有効にするかどうか
    /// </summary>
    public bool EnableAutoOrientation { get; set; } = false;

    /// <summary>
    /// スクリーンショット保存先フォルダ
    /// </summary>
    public string ScreenshotDirectory { get; set; } = 
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "SimpleMirror");

    /// <summary>
    /// クリップボードにもスクリーンショットをコピーするかどうか
    /// </summary>
    public bool CopyScreenshotToClipboard { get; set; } = true;

    /// <summary>
    /// アプリ起動時にAirPlayサーバーを自動起動するかどうか
    /// </summary>
    public bool AutoStartServer { get; set; } = true;

    /// <summary>
    /// 画面回転角度（0, 90, 180, 270）
    /// </summary>
    public int RotationDegrees { get; set; } = 0;

    /// <summary>
    /// ウィンドウ幅
    /// </summary>
    public double WindowWidth { get; set; } = 480;

    /// <summary>
    /// ウィンドウ高さ
    /// </summary>
    public double WindowHeight { get; set; } = 840;
}
