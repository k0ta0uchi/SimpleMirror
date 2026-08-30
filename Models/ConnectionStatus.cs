namespace SimpleMirror.Models;

/// <summary>
/// AirPlay接続ステータス
/// </summary>
public enum ConnectionStatus
{
    /// <summary>
    /// サーバー停止中
    /// </summary>
    Stopped,

    /// <summary>
    /// 接続待機中（iPhoneからのミラーリング開始をリスニング）
    /// </summary>
    Listening,

    /// <summary>
    /// iPhoneと接続確立・ミラーリング中
    /// </summary>
    Connected,

    /// <summary>
    /// 一時停止中
    /// </summary>
    Paused,

    /// <summary>
    /// エラー発生
    /// </summary>
    Error
}
