#include <windows.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <stdio.h>
#include <initguid.h>

// {7DE97D1F-66CA-415B-9C8C-4D4D57504687}
static const CLSID CLSID_Preview = { 0x7de97d1f, 0x66ca, 0x415b, { 0x9c, 0x8c, 0x4d, 0x4d, 0x57, 0x50, 0x46, 0x87 } };

int wmain(int argc, wchar_t** argv)
{
    if (argc < 2) { wprintf(L"usage: NativePreviewE2eTest <file>\n"); return 1; }
    CoInitialize(nullptr);

    IInitializeWithFile* init = nullptr;
    HRESULT hr = CoCreateInstance(CLSID_Preview, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&init));
    wprintf(L"CoCreateInstance(IInitializeWithFile) hr=0x%08X\n", hr);
    if (FAILED(hr)) return 2;

    hr = init->Initialize(argv[1], 0);
    wprintf(L"Initialize hr=0x%08X\n", hr);

    IPreviewHandler* preview = nullptr;
    hr = init->QueryInterface(IID_PPV_ARGS(&preview));
    wprintf(L"QueryInterface(IPreviewHandler) hr=0x%08X\n", hr);
    if (FAILED(hr)) return 3;

    RECT rc = { 0, 0, 800, 600 };
    hr = preview->SetWindow(GetDesktopWindow(), &rc);
    wprintf(L"SetWindow hr=0x%08X\n", hr);

    hr = preview->DoPreview();
    wprintf(L"DoPreview hr=0x%08X\n", hr);
    wprintf(L"RESULT: %s\n", SUCCEEDED(hr) ? L"SUCCESS" : L"FAILURE");

    // Let the spawned process run a moment, then unload.
    Sleep(3000);
    preview->Unload();
    preview->Release();
    init->Release();
    CoUninitialize();
    return 0;
}
