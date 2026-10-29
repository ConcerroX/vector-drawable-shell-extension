using System.Drawing;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using VectorDrawableConverter;

namespace VectorDrawable.PreviewHandler;

/// <summary>
/// A borderless form that hosts a WebView2 control rendering a VectorDrawable file. The form is
/// parented into the Explorer preview pane by the native COM DLL (see VectorDrawableShellExt).
/// </summary>
public sealed class VectorDrawablePreviewControl : Form
{
    private static readonly VectorDrawableToSvgConverter Converter = new();

    private WebView2? _browser;
    private readonly string _userDataFolder;

    public VectorDrawablePreviewControl()
    {
        // WebView2 runs under prevhost.exe (the out-of-process preview host), which is a
        // LOW-integrity process. It can only write to LocalLow, not LocalAppData — writing the
        // user-data folder under LocalAppData fails with "resource in use" because the WebView2
        // child processes can't create their files there. PowerToys uses LocalLow for exactly
        // this reason. Keep the per-process suffix so concurrent previews never collide.
        _userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low",
            "VectorDrawableShellExtension", "WebView2Preview",
            Environment.ProcessId.ToString());

        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.White;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

        // Force the window handle to be created on this (STA) thread, matching PowerToys'
        // FormHandlerControl constructor.
        _ = Handle;
    }

    /// <summary>
    /// Renders <paramref name="filePath"/> into the control. The WebView2 loads an SVG converted
    /// from the VectorDrawable XML.
    /// </summary>
    public void DoPreview(string filePath)
    {
        string? svg;
        try
        {
            string xml = File.ReadAllText(filePath);
            svg = Converter.Convert(xml);
        }
        catch (Exception ex)
        {
            Log("convert error: " + ex);
            svg = null;
        }

        if (svg is null)
        {
            ShowError("无法预览：不是有效的 Vector Drawable XML");
            return;
        }

        Log("converted ok, svg length=" + svg.Length);
        ShowBrowser(svg);
    }

    private void ShowBrowser(string svg)
    {
        Log("ShowBrowser start");
        _browser = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.Transparent,
        };

        var options = new CoreWebView2EnvironmentOptions("--block-new-web-contents");
        Log("creating env...");
        var envAwaiter = CoreWebView2Environment
            .CreateAsync(userDataFolder: _userDataFolder, options: options)
            .ConfigureAwait(true)
            .GetAwaiter();

        envAwaiter.OnCompleted(async () =>
        {
            try
            {
                Log("env completed, getting result...");
                var env = envAwaiter.GetResult();
                Log("env result ok, EnsureCoreWebView2Async...");
                await _browser.EnsureCoreWebView2Async(env).ConfigureAwait(true);
                Log("EnsureCoreWebView2Async done");
                _browser.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                _browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _browser.CoreWebView2.Settings.AreHostObjectsAllowed = false;
                _browser.CoreWebView2.Settings.IsScriptEnabled = false;

                _browser.NavigateToString(WrapInHtml(svg));
                Controls.Add(_browser);
                Log("browser added to controls");
            }
            catch (Exception ex)
            {
                Log("preview error: " + ex);
                ShowError("预览渲染失败: " + ex.Message);
            }
        });
    }

    private static void Log(string msg)
    {
        try
        {
            // Write to %TEMP% (always writable, even under prevhost.exe / low-integrity hosts)
            // rather than AppContext.BaseDirectory, which may be read-only for Explorer's
            // out-of-process preview host.
            string logPath = Path.Combine(Path.GetTempPath(), "VectorDrawablePreview.log");
            File.AppendAllText(
                logPath,
                DateTime.Now.ToString("HH:mm:ss.fff") + " [" + Environment.ProcessId + "] " + msg + Environment.NewLine);
        }
        catch { }
    }

    private void ShowError(string message)
    {
        Controls.Clear();
        var label = new Label
        {
            Text = message,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        Controls.Add(label);
    }

    private static string WrapInHtml(string svg) =>
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head>" +
        "<body style=\"margin:0;padding:0;overflow:hidden;\">" + svg + "</body></html>";
}
