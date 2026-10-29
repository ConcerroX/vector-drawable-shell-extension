using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;

namespace VectorDrawable.ThumbnailProvider;

/// <summary>
/// Entry point for the thumbnail provider EXE. Invoked by the native COM DLL with two
/// arguments: <c>&lt;input-file&gt; &lt;size&gt;</c>. On success writes <c>&lt;input-file&gt;.png</c>.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            ApplicationConfiguration.Initialize();

            // PowerToys: reset CWD to this handler's own install dir — Explorer's inherited CWD
            // can be an ACL-restricted folder that breaks WebView2's sandboxed child processes.
            try
            {
                System.IO.Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.Security.SecurityException or System.ArgumentException)
            {
            }

            if (args.Length != 2)
            {
                return 1;
            }

            string filePath = args[0];
            if (!uint.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint cx))
            {
                return 1;
            }

            var thumb = new VectorDrawableThumbnail(filePath);
            using Bitmap? bmp = thumb.GetThumbnail(cx);
            if (bmp is null)
            {
                return 2;
            }

            string outPath = filePath.Replace(".xml", ".png", StringComparison.OrdinalIgnoreCase);
            bmp.Save(outPath, ImageFormat.Png);
            return 0;
        }
        catch
        {
            return 3;
        }
    }
}
