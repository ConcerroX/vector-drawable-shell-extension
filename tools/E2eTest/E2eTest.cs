using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

[ComImport]
[Guid("E357FCCD-A995-4576-B01F-234630154E96")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IThumbnailProvider
{
    [PreserveSig]
    int GetThumbnail(uint cx, out IntPtr phbmp, out uint pdwAlpha);
}

[ComImport]
[Guid("B824B49D-22AC-4161-AC8A-9916E8FA3F7F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IInitializeWithStream
{
    [PreserveSig]
    int Initialize(IStream pstream, uint grfMode);
}

class E2eTest
{
    static readonly Guid CLSID_Thumbnail = new("2D063192-2936-47B0-9029-EEC429B56CAD");
    const uint CLSCTX_INPROC_SERVER = 0x1;
    const uint STGM_READ = 0x00000000;
    const uint STGM_SHARE_DENY_NONE = 0x00000040;

    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int SHCreateStreamOnFileEx(string pszFile, uint grfMode, uint dwAttributes, bool fCreate, IntPtr pstmTemplate, out IStream ppstm);

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("usage: E2eTest <file>"); return 1; }
        string file = args[0];

        var clsid = CLSID_Thumbnail;
        var iidInit = typeof(IInitializeWithStream).GUID;
        var iidThumb = typeof(IThumbnailProvider).GUID;

        int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iidInit, out IntPtr initPtr);
        Console.WriteLine($"CoCreateInstance(IInitializeWithStream) hr=0x{hr:X8}");
        if (hr != 0) return 2;

        var init = (IInitializeWithStream)Marshal.GetObjectForIUnknown(initPtr);

        hr = SHCreateStreamOnFileEx(file, STGM_READ | STGM_SHARE_DENY_NONE, 0, false, IntPtr.Zero, out IStream stream);
        Console.WriteLine($"SHCreateStreamOnFileEx hr=0x{hr:X8}");
        if (hr != 0) return 3;

        hr = init.Initialize(stream, STGM_READ);
        Console.WriteLine($"Initialize hr=0x{hr:X8}");
        if (hr != 0) return 4;

        // Cast the same object to IThumbnailProvider (it implements both interfaces).
        var thumb = (IThumbnailProvider)init;
        hr = thumb.GetThumbnail(256, out IntPtr hbmp, out uint alphaType);
        Console.WriteLine($"GetThumbnail hr=0x{hr:X8} hbmp={(hbmp != IntPtr.Zero ? "OK" : "NULL")} alphaType={alphaType}");

        if (hbmp != IntPtr.Zero)
        {
            Console.WriteLine("RESULT: SUCCESS");
            return 0;
        }
        Console.WriteLine("RESULT: FAILURE");
        return 6;
    }
}
