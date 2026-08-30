using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using SimpleMirror.Interop;

namespace SimpleMirror.Services;

/// <summary>
/// ミラーリング映像のスクリーンショット撮影・保存・クリップボード連携サービス
/// </summary>
public class CaptureService
{
    public async Task<string?> CaptureWindowAsync(IntPtr hWnd, string saveDirectory, bool copyToClipboard = true)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd))
                {
                    return null;
                }

                var (width, height) = GetWindowDimensions(hWnd);
                if (width <= 0 || height <= 0)
                {
                    return null;
                }

                using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bitmap))
                {
                    IntPtr hdc = g.GetHdc();
                    try
                    {
                        // 2 = PW_RENDERFULLCONTENT (DirectX / GPU レンダリング対応)
                        NativeMethods.PrintWindow(hWnd, hdc, 2);
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                }

                string fullPath = SaveBitmapToFile(bitmap, saveDirectory);

                if (copyToClipboard)
                {
                    CopyBitmapToClipboard(bitmap);
                }

                return fullPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CaptureService] Capture error: {ex.Message}");
                return null;
            }
        });
    }

    private static (int width, int height) GetWindowDimensions(IntPtr hWnd)
    {
        NativeMethods.GetClientRect(hWnd, out var clientRect);
        int width = clientRect.Width;
        int height = clientRect.Height;

        if (width <= 0 || height <= 0)
        {
            NativeMethods.GetWindowRect(hWnd, out var windowRect);
            width = windowRect.Width;
            height = windowRect.Height;
        }

        return (width, height);
    }

    private static string SaveBitmapToFile(Bitmap bitmap, string saveDirectory)
    {
        if (!Directory.Exists(saveDirectory))
        {
            Directory.CreateDirectory(saveDirectory);
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string filename = $"SimpleMirror_{timestamp}.png";
        string fullPath = Path.Combine(saveDirectory, filename);

        bitmap.Save(fullPath, ImageFormat.Png);
        return fullPath;
    }

    private static void CopyBitmapToClipboard(Bitmap bitmap)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                Clipboard.SetImage(bitmapSource);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CaptureService] Clipboard error: {ex.Message}");
            }
            finally
            {
                // GDI ハンドルのメモリリークを確実に防止
                NativeMethods.DeleteObject(hBitmap);
            }
        });
    }
}
