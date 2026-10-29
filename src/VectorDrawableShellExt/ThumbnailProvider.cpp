#include "pch.h"
#include "ThumbnailProvider.h"

#include <filesystem>
#include <fstream>
#include <shellapi.h>
#include <sstream>
#include <cctype>

namespace fs = std::filesystem;

namespace {
bool ContainsVectorRoot(const std::string& content);
void DllLog(const std::wstring& msg);
}

ThumbnailProvider::ThumbnailProvider() : m_cRef(1), m_pStream(nullptr)
{
    InterlockedIncrement(&g_cDllRef);
}

ThumbnailProvider::~ThumbnailProvider()
{
    if (m_pStream)
    {
        m_pStream->Release();
    }
    InterlockedDecrement(&g_cDllRef);
}

IFACEMETHODIMP ThumbnailProvider::QueryInterface(REFIID riid, void** ppv)
{
    static const QITAB qit[] = {
        QITABENT(ThumbnailProvider, IThumbnailProvider),
        QITABENT(ThumbnailProvider, IInitializeWithStream),
        { nullptr },
    };
    return QISearch(this, qit, riid, ppv);
}

IFACEMETHODIMP_(ULONG) ThumbnailProvider::AddRef()
{
    return InterlockedIncrement(&m_cRef);
}

IFACEMETHODIMP_(ULONG) ThumbnailProvider::Release()
{
    ULONG cRef = InterlockedDecrement(&m_cRef);
    if (cRef == 0)
    {
        delete this;
    }
    return cRef;
}

IFACEMETHODIMP ThumbnailProvider::Initialize(IStream* pStream, DWORD /*grfMode*/)
{
    if (pStream == nullptr)
    {
        return E_INVALIDARG;
    }

    if (m_pStream)
    {
        m_pStream->Release();
        m_pStream = nullptr;
    }

    m_pStream = pStream;
    m_pStream->AddRef();
    return S_OK;
}

IFACEMETHODIMP ThumbnailProvider::GetThumbnail(UINT cx, HBITMAP* phbmp, WTS_ALPHATYPE* pdwAlpha)
{
    if (phbmp == nullptr || pdwAlpha == nullptr || m_pStream == nullptr)
    {
        return E_INVALIDARG;
    }

    *phbmp = nullptr;
    *pdwAlpha = WTSAT_UNKNOWN;

    // Locate this DLL's directory so we can find the sibling .NET EXE.
    wchar_t modulePath[MAX_PATH];
    GetModuleFileNameW(g_hInst, modulePath, MAX_PATH);
    fs::path dllDir = fs::path(modulePath).parent_path();

    // Create a unique temp file.
    GUID guid{};
    if (FAILED(CoCreateGuid(&guid)))
    {
        return E_FAIL;
    }

    wchar_t guidStr[64];
    if (StringFromGUID2(guid, guidStr, 64) == 0)
    {
        return E_FAIL;
    }

    wchar_t tempDir[MAX_PATH];
    DWORD tempLen = GetTempPathW(MAX_PATH, tempDir);
    if (tempLen == 0 || tempLen > MAX_PATH)
    {
        return E_FAIL;
    }

    std::wstring xmlPath = std::wstring(tempDir) + guidStr + L".xml";
    std::wstring bmpPath = std::wstring(tempDir) + guidStr + L".png";

    // Write the stream to the temp XML file, and sniff whether it is a VectorDrawable.
    bool isVector = false;
    {
        std::ofstream out(xmlPath, std::ios::binary);
        if (!out.is_open())
        {
            return E_FAIL;
        }

        char buffer[4096];
        ULONG cbRead = 0;
        std::string head;
        while (true)
        {
            HRESULT hr = m_pStream->Read(buffer, sizeof(buffer), &cbRead);
            if (cbRead > 0)
            {
                out.write(buffer, cbRead);
                if (head.size() < 4096)
                {
                    head.append(buffer, cbRead);
                }
            }
            if (hr != S_OK || cbRead == 0)
            {
                break;
            }
        }
        out.close();

        // Content sniff: only proceed if the root element is <vector>.
        isVector = ContainsVectorRoot(head);
    }

    if (!isVector)
    {
        DeleteFileW(xmlPath.c_str());
        return E_FAIL; // Not a VectorDrawable: let the default .xml handler take over.
    }

    // Spawn the .NET thumbnail EXE and wait for it.
    std::wstring exePath = (dllDir / L"VectorDrawableThumbnailProvider.exe").wstring();
    std::wstring cmdLine = L"\"" + xmlPath + L"\" " + std::to_wstring(cx);

    DllLog(L"exePath=" + exePath + L" cmdLine=" + cmdLine);

    SHELLEXECUTEINFOW sei{ sizeof(sei) };
    sei.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_NO_UI;
    sei.lpFile = exePath.c_str();
    sei.lpParameters = cmdLine.c_str();
    sei.nShow = SW_HIDE;

    HBITMAP hbmp = nullptr;
    if (ShellExecuteExW(&sei) && sei.hProcess != nullptr)
    {
        DllLog(L"ShellExecuteEx OK, waiting...");
        WaitForSingleObject(sei.hProcess, 30000); // 30s timeout
        CloseHandle(sei.hProcess);

        // Load the PNG via GDI+ and blit it onto a 32bpp top-down DIB section so the alpha
        // channel is preserved. GDI+'s Bitmap::GetHBITMAP loses alpha (produces a DDB), which
        // is exactly what turned the transparent background black. CreateDIBSection keeps the
        // per-pixel alpha byte, so Explorer renders it as a true transparent thumbnail.
        Gdiplus::GdiplusStartupInput gdiplusStartupInput;
        ULONG_PTR gdiplusToken = 0;
        Gdiplus::GdiplusStartup(&gdiplusToken, &gdiplusStartupInput, nullptr);

        {
            // Scope all GDI+ objects so they are destructed BEFORE GdiplusShutdown below.
            Gdiplus::Bitmap png(bmpPath.c_str());
            if (png.GetLastStatus() == Gdiplus::Ok)
            {
                int w = png.GetWidth();
                int h = png.GetHeight();

                BITMAPINFO bmi{};
                bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
                bmi.bmiHeader.biWidth = w;
                bmi.bmiHeader.biHeight = -h; // negative => top-down, so row order matches the PNG
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = BI_RGB;

                void* bits = nullptr;
                HDC screenDc = GetDC(nullptr);
                hbmp = CreateDIBSection(screenDc, &bmi, DIB_RGB_COLORS, &bits, nullptr, 0);
                ReleaseDC(nullptr, screenDc);

                if (hbmp && bits)
                {
                    HDC memDc = CreateCompatibleDC(nullptr);
                    HGDIOBJ old = SelectObject(memDc, hbmp);

                    {
                        Gdiplus::Graphics g(memDc);
                        g.Clear(Gdiplus::Color(0, 0, 0, 0)); // transparent background
                        g.DrawImage(&png, 0, 0, w, h);
                        g.Flush();
                    }

                    SelectObject(memDc, old);
                    DeleteDC(memDc);
                }

                DllLog(hbmp ? L"DIB section created (alpha preserved)" : L"CreateDIBSection FAILED");
            }
            else
            {
                DllLog(L"GDI+ PNG load failed, status=" + std::to_wstring(static_cast<int>(png.GetLastStatus())));
            }
        } // png destructed here

        Gdiplus::GdiplusShutdown(gdiplusToken);
    }
    else
    {
        DllLog(L"ShellExecuteEx FAILED, err=" + std::to_wstring(GetLastError()));
    }

    // Clean up temp files.
    DeleteFileW(xmlPath.c_str());
    DeleteFileW(bmpPath.c_str());

    if (hbmp == nullptr)
    {
        return E_FAIL;
    }

    *phbmp = hbmp;
    *pdwAlpha = WTSAT_ARGB;
    return S_OK;
}

namespace {

void DllLog(const std::wstring& msg)
{
    std::wofstream log(L"D:\\Projects\\vector-drawable-shell-extension\\deploy\\dll_debug.log", std::ios::app);
    if (log.is_open())
    {
        log << msg << L"\n";
        log.close();
    }
}

// Case-insensitive substring search.
bool IEquals(std::string a, std::string b)
{
    if (a.size() != b.size())
    {
        return false;
    }
    for (size_t i = 0; i < a.size(); i++)
    {
        if (tolower(static_cast<unsigned char>(a[i])) != tolower(static_cast<unsigned char>(b[i])))
        {
            return false;
        }
    }
    return true;
}

// Returns true if the XML content's root element is <vector> (or <vector ...>). This is a
// lightweight scan of the first non-declaration tag, matching what VectorDrawable requires.
bool ContainsVectorRoot(const std::string& content)
{
    size_t i = 0;
    size_t n = content.size();

    // Skip the optional XML prolog / DOCTYPE.
    while (i < n)
    {
        size_t lt = content.find('<', i);
        if (lt == std::string::npos)
        {
            return false;
        }
        // If it's a comment or processing instruction, skip past it; otherwise this is the root.
        if (lt + 1 < n && (content[lt + 1] == '?' || content[lt + 1] == '!'))
        {
            size_t gt = content.find('>', lt + 1);
            if (gt == std::string::npos)
            {
                return false;
            }
            i = gt + 1;
            continue;
        }
        // Root open tag starts at lt+1.
        size_t start = lt + 1;
        size_t end = start;
        while (end < n && content[end] != ' ' && content[end] != '\t' && content[end] != '\r' && content[end] != '\n' && content[end] != '>' && content[end] != '/')
        {
            end++;
        }
        std::string tag = content.substr(start, end - start);
        return IEquals(tag, "vector");
    }
    return false;
}

} // namespace

