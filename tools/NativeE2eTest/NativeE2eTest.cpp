#include <windows.h>
#include <shobjidl.h>
#include <thumbcache.h>
#include <propsys.h>
#include <shlwapi.h>
#include <stdio.h>

// Native E2E test: CoCreateInstance the registered thumbnail provider and drive
// IInitializeWithStream -> GetThumbnail, verifying the returned HBITMAP has real
// per-pixel alpha (transparent background preserved).

static const CLSID CLSID_Thumbnail = { 0x2d063192, 0x2936, 0x47b0, { 0x90, 0x29, 0xee, 0xc4, 0x29, 0xb5, 0x6c, 0xad } };

int wmain(int argc, wchar_t** argv)
{
    if (argc < 2) { wprintf(L"usage: NativeE2eTest <file>\n"); return 1; }

    CoInitialize(nullptr);

    IInitializeWithStream* init = nullptr;
    HRESULT hr = CoCreateInstance(CLSID_Thumbnail, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&init));
    wprintf(L"CoCreateInstance(IInitializeWithStream) hr=0x%08X\n", hr);
    if (FAILED(hr)) return 2;

    IStream* stream = nullptr;
    hr = SHCreateStreamOnFileEx(argv[1], STGM_READ | STGM_SHARE_DENY_NONE, 0, FALSE, nullptr, &stream);
    wprintf(L"SHCreateStreamOnFileEx hr=0x%08X\n", hr);
    if (FAILED(hr)) return 3;

    hr = init->Initialize(stream, STGM_READ);
    wprintf(L"Initialize hr=0x%08X\n", hr);
    if (FAILED(hr)) return 4;

    IThumbnailProvider* thumb = nullptr;
    hr = init->QueryInterface(IID_PPV_ARGS(&thumb));
    wprintf(L"QueryInterface(IThumbnailProvider) hr=0x%08X\n", hr);
    if (FAILED(hr)) return 5;

    HBITMAP hbmp = nullptr;
    WTS_ALPHATYPE alphaType = WTSAT_UNKNOWN;
    hr = thumb->GetThumbnail(256, &hbmp, &alphaType);
    wprintf(L"GetThumbnail hr=0x%08X hbmp=%s alphaType=%d\n", hr, hbmp ? L"OK" : L"NULL", (int)alphaType);
    if (FAILED(hr) || !hbmp) return 6;

    // Inspect the bitmap's alpha channel via GetObject (DIB section).
    DIBSECTION ds{};
    if (GetObjectW(hbmp, sizeof(ds), &ds) == sizeof(ds) && ds.dsBm.bmBits)
    {
        int w = ds.dsBm.bmWidth, h = ds.dsBm.bmHeight;
        int pitch = ds.dsBm.bmWidthBytes;
        unsigned char* bits = (unsigned char*)ds.dsBm.bmBits;
        int transparent = 0, icon = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                unsigned char a = bits[y * pitch + x * 4 + 3];
                if (a < 10) transparent++; else icon++;
            }
        }
        wprintf(L"bitmap %dx%d bpp=%d transparent=%d icon=%d\n", w, h, ds.dsBm.bmBitsPixel, transparent, icon);
        wprintf(L"RESULT: %s\n", (transparent > 0 && icon > 0) ? L"SUCCESS (alpha preserved)" : L"FAILURE (no alpha)");
    }
    else
    {
        wprintf(L"RESULT: FAILURE (not a DIB section)\n");
        return 7;
    }

    thumb->Release();
    init->Release();
    stream->Release();
    CoUninitialize();
    return 0;
}
