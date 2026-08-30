namespace SimpleMirror.Models;

/// <summary>
/// 現在のミラーリングセッション情報
/// </summary>
public class MirrorSessionInfo
{
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceModel { get; set; } = "iPhone";
    public string ClientIp { get; set; } = string.Empty;
    public int Width { get; set; } = 1170;
    public int Height { get; set; } = 2532;
    public int Fps { get; set; } = 60;
    public DateTime ConnectedAt { get; set; } = DateTime.Now;

    public string ResolutionText => Width > 0 && Height > 0 ? $"{Width} × {Height}" : "---";
    public string FpsText => Fps > 0 ? $"{Fps} FPS" : "---";
    public bool IsPortrait => Height >= Width;
}
