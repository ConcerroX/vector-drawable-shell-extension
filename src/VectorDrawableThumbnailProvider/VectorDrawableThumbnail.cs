using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using VectorDrawableConverter;

namespace VectorDrawable.ThumbnailProvider;

/// <summary>
/// Renders an Android VectorDrawable XML file to a bitmap by converting it to SVG and
/// rasterizing via WebView2 (Chromium), mirroring PowerToys' SVG thumbnail provider.
/// </summary>
public sealed class VectorDrawableThumbnail
{
    private const uint MaxThumbnailSize = 10000;
    private static readonly VectorDrawableToSvgConverter Converter = new();

    private readonly string _filePath;
    private readonly string _userDataFolder;

    public VectorDrawableThumbnail(string filePath)
    {
        _filePath = filePath;
        // Per-process user-data folder under LocalLow. Thumbnails are also rendered under a
        // low-integrity host (dllhost.exe), which can only write to LocalLow — not LocalAppData.
        // A unique folder per process avoids concurrent thumbnails colliding on the WebView2 lock.
        _userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low",
            "VectorDrawableShellExtension", "WebView2Thumbnail",
            Environment.ProcessId.ToString());
    }

    public Bitmap? GetThumbnail(uint cx)
    {
        if (cx == 0 || cx > MaxThumbnailSize)
        {
            return null;
        }

        // 1. Read + convert the VectorDrawable to SVG.
        string? svg;
        try
        {
            string xml = File.ReadAllText(_filePath);
            svg = Converter.Convert(xml);
        }
        catch (Exception ex)
        {
            Log("convert failed: " + ex.Message);
            return null;
        }

        // 2. Rasterize via WebView2.
        try
        {
            return Rasterize(svg, (int)cx);
        }
        catch (Exception ex)
        {
            Log("rasterize failed: " + ex.Message);
            return null;
        }
    }

    private static void Log(string msg)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "thumb_debug.log"),
                DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine);
        }
        catch { }
    }

    private Bitmap? Rasterize(string svg, int size)
    {
        Bitmap? thumbnail = null;
        var done = new ManualResetEventSlim(false);

        var browser = new WebView2
        {
            Dock = DockStyle.Fill,
            Visible = true,
            Width = size,
            Height = size,
            DefaultBackgroundColor = Color.Transparent,
        };

        browser.NavigationCompleted += async (sender, args) =>
        {
            try
            {
                // The converter emits a centered, size-capped SVG for the preview pane. For the
                // thumbnail we override that inline style to fill the fixed canvas exactly, and
                // clear the centering transform so the icon occupies the whole thumbnail.
                await browser.ExecuteScriptAsync(
                    "var s=document.querySelector('svg');" +
                    "s.style.maxWidth='none';s.style.maxHeight='none';" +
                    "s.style.width='100%';s.style.height='100%';" +
                    "s.style.position='static';s.style.transform='none';" +
                    "document.body.style.overflow='hidden';" +
                    "document.body.style.margin='0';");

                using var ms = new MemoryStream();
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
                ms.Position = 0;
                thumbnail = new Bitmap(ms);
            }
            catch (Exception ex)
            {
                Log("nav completed error: " + ex.Message);
                thumbnail = null;
            }
            finally
            {
                done.Set();
            }
        };

        var options = new CoreWebView2EnvironmentOptions("--block-new-web-contents");
        var envAwaiter = CoreWebView2Environment
            .CreateAsync(userDataFolder: _userDataFolder, options: options)
            .ConfigureAwait(true)
            .GetAwaiter();

        envAwaiter.OnCompleted(async () =>
        {
            try
            {
                var env = envAwaiter.GetResult();
                await browser.EnsureCoreWebView2Async(env).ConfigureAwait(true);
                browser.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                browser.CoreWebView2.Settings.AreHostObjectsAllowed = false;
                browser.CoreWebView2.Settings.IsScriptEnabled = false;
                browser.CoreWebView2.Settings.IsWebMessageEnabled = false;

                browser.NavigateToString(WrapInHtml(svg));
            }
            catch (Exception ex)
            {
                Log("env completed error: " + ex.Message);
                done.Set();
            }
        });

        while (!done.Wait(75))
        {
            Application.DoEvents();
        }

        browser.Dispose();
        return thumbnail;
    }

    private static string WrapInHtml(string svg) =>
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head>" +
        "<body style=\"margin:0;padding:0;overflow:hidden;\">" + svg + "</body></html>";
}
