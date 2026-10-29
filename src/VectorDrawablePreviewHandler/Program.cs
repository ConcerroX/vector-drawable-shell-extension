using System.Globalization;
using System.Runtime.InteropServices;

namespace VectorDrawable.PreviewHandler;

/// <summary>
/// Entry point for the preview handler EXE. Invoked by the native COM DLL with six arguments:
/// <c>&lt;file&gt; &lt;hwndHex&gt; &lt;left&gt; &lt;right&gt; &lt;top&gt; &lt;bottom&gt;</c>.
/// The form is parented into the Explorer-provided HWND and renders the file.
/// </summary>
internal static class Program
{
    private const int GWL_STYLE = -16;
    private const long WS_CHILD = 0x40000000;

    [DllImport("user32.dll")]
    private static extern long GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern long SetWindowLongPtr(IntPtr hWnd, int nIndex, long dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const uint SWP_NOZORDER = 0x0004;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            ApplicationConfiguration.Initialize();

            // PowerToys: the out-of-process preview host inherits Explorer's working directory,
            // which can be an AppContainer-ACL-restricted folder that breaks WebView2's sandboxed
            // child processes. Reset it to this handler's install directory.
            try
            {
                System.IO.Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.Security.SecurityException or System.ArgumentException)
            {
            }

            if (args.Length != 6)
            {
                return 1;
            }

            string filePath = args[0];
            IntPtr parentHwnd = new(long.Parse(args[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            int left = int.Parse(args[2], CultureInfo.InvariantCulture);
            int right = int.Parse(args[3], CultureInfo.InvariantCulture);
            int top = int.Parse(args[4], CultureInfo.InvariantCulture);
            int bottom = int.Parse(args[5], CultureInfo.InvariantCulture);

            using var control = new VectorDrawablePreviewControl();

            // Make the form a child of the Explorer preview pane HWND.
            long style = GetWindowLongPtr(control.Handle, GWL_STYLE);
            if ((style & WS_CHILD) == 0)
            {
                SetWindowLongPtr(control.Handle, GWL_STYLE, style | WS_CHILD);
            }

            SetParent(control.Handle, parentHwnd);
            SetWindowPos(control.Handle, IntPtr.Zero, left, top, right - left, bottom - top, SWP_NOZORDER);

            control.DoPreview(filePath);
            control.Show();

            Application.Run(control);
            return 0;
        }
        catch
        {
            return 3;
        }
    }
}
