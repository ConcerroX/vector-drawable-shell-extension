#include "pch.h"
#include "PreviewHandler.h"

#include <filesystem>
#include <fstream>
#include <shellapi.h>
#include <sstream>
#include <iomanip>
#include <cctype>

namespace fs = std::filesystem;

namespace {
bool IsVectorFile(const std::wstring& path);
void PreviewLog(const std::wstring& msg);
}

PreviewHandler::PreviewHandler()
    : m_cRef(1)
    , m_hwndParent(nullptr)
    , m_rcParent{}
    , m_punkSite(nullptr)
    , m_process(nullptr)
    , m_launched(false)
{
    InterlockedIncrement(&g_cDllRef);
}

PreviewHandler::~PreviewHandler()
{
    if (m_process)
    {
        CloseHandle(m_process);
    }
    if (m_punkSite)
    {
        m_punkSite->Release();
    }
    InterlockedDecrement(&g_cDllRef);
}

IFACEMETHODIMP PreviewHandler::QueryInterface(REFIID riid, void** ppv)
{
    static const QITAB qit[] = {
        QITABENT(PreviewHandler, IPreviewHandler),
        QITABENT(PreviewHandler, IInitializeWithFile),
        QITABENT(PreviewHandler, IObjectWithSite),
        { nullptr },
    };
    return QISearch(this, qit, riid, ppv);
}

IFACEMETHODIMP_(ULONG) PreviewHandler::AddRef()
{
    return InterlockedIncrement(&m_cRef);
}

IFACEMETHODIMP_(ULONG) PreviewHandler::Release()
{
    ULONG cRef = InterlockedDecrement(&m_cRef);
    if (cRef == 0)
    {
        delete this;
    }
    return cRef;
}

IFACEMETHODIMP PreviewHandler::Initialize(LPCWSTR pszFilePath, DWORD /*grfMode*/)
{
    if (pszFilePath == nullptr)
    {
        return E_INVALIDARG;
    }
    m_filePath = pszFilePath;
    PreviewLog(L"Initialize: " + m_filePath);
    return S_OK;
}

IFACEMETHODIMP PreviewHandler::SetSite(IUnknown* pUnkSite)
{
    if (m_punkSite)
    {
        m_punkSite->Release();
        m_punkSite = nullptr;
    }
    if (pUnkSite)
    {
        pUnkSite->QueryInterface(IID_PPV_ARGS(&m_punkSite));
    }
    return S_OK;
}

IFACEMETHODIMP PreviewHandler::GetSite(REFIID riid, void** ppv)
{
    if (ppv == nullptr)
    {
        return E_POINTER;
    }
    *ppv = nullptr;
    return m_punkSite ? m_punkSite->QueryInterface(riid, ppv) : E_FAIL;
}

IFACEMETHODIMP PreviewHandler::SetWindow(HWND hwnd, const RECT* prc)
{
    if (hwnd && prc)
    {
        m_hwndParent = hwnd;
        m_rcParent = *prc;
        PreviewLog(L"SetWindow hwnd=" + std::to_wstring((uintptr_t)hwnd));
    }
    return S_OK;
}

IFACEMETHODIMP PreviewHandler::SetRect(const RECT* prc)
{
    if (prc == nullptr)
    {
        return E_INVALIDARG;
    }

    // Only relaunch if the size actually changed and we already launched once.
    if (m_launched && memcmp(&m_rcParent, prc, sizeof(RECT)) != 0)
    {
        m_rcParent = *prc;
        return Launch();
    }

    m_rcParent = *prc;
    return S_OK;
}

IFACEMETHODIMP PreviewHandler::DoPreview()
{
    PreviewLog(L"DoPreview: hwnd=" + std::to_wstring((uintptr_t)m_hwndParent) + L" file=" + m_filePath);
    if (m_hwndParent == nullptr)
    {
        PreviewLog(L"DoPreview: no hwnd -> E_FAIL");
        return E_FAIL;
    }

    // Content sniff: only preview files whose root element is <vector>.
    if (!IsVectorFile(m_filePath))
    {
        PreviewLog(L"DoPreview: not a vector -> E_FAIL");
        return E_FAIL;
    }

    PreviewLog(L"DoPreview: launching...");
    return Launch();
}

IFACEMETHODIMP PreviewHandler::Unload()
{
    if (m_process)
    {
        // Gracefully close the preview EXE (post WM_CLOSE) so WebView2 tears down its child
        // msedgewebview2.exe processes and releases the user-data-folder lock. TerminateProcess
        // left those children orphaned and holding the lock, causing "resource in use" on the
        // next preview.
        DWORD pid = GetProcessId(m_process);
        EnumWindows([](HWND hwnd, LPARAM lParam) -> BOOL {
            DWORD wpid = 0;
            GetWindowThreadProcessId(hwnd, &wpid);
            if (wpid == static_cast<DWORD>(lParam))
            {
                PostMessageW(hwnd, WM_CLOSE, 0, 0);
            }
            return TRUE;
        }, static_cast<LPARAM>(pid));

        // Give it a moment to exit, then force-close as a fallback.
        if (WaitForSingleObject(m_process, 2000) == WAIT_TIMEOUT)
        {
            TerminateProcess(m_process, 0);
        }
        CloseHandle(m_process);
        m_process = nullptr;
    }
    m_hwndParent = nullptr;
    m_launched = false;
    return S_OK;
}

IFACEMETHODIMP PreviewHandler::SetFocus()
{
    return S_OK;
}

IFACEMETHODIMP PreviewHandler::QueryFocus(HWND* phwnd)
{
    if (phwnd == nullptr)
    {
        return E_INVALIDARG;
    }
    *phwnd = GetFocus();
    return S_OK;
}

IFACEMETHODIMP PreviewHandler::TranslateAccelerator(MSG* pmsg)
{
    if (m_punkSite)
    {
        IPreviewHandlerFrame* pFrame = nullptr;
        if (SUCCEEDED(m_punkSite->QueryInterface(IID_PPV_ARGS(&pFrame))) && pFrame)
        {
            HRESULT hr = pFrame->TranslateAccelerator(pmsg);
            pFrame->Release();
            return hr;
        }
    }
    return S_FALSE;
}

HRESULT PreviewHandler::Launch()
{
    // Gracefully close any previous instance (preview can be re-requested on resize/refocus).
    if (m_process)
    {
        DWORD pid = GetProcessId(m_process);
        EnumWindows([](HWND hwnd, LPARAM lParam) -> BOOL {
            DWORD wpid = 0;
            GetWindowThreadProcessId(hwnd, &wpid);
            if (wpid == static_cast<DWORD>(lParam))
            {
                PostMessageW(hwnd, WM_CLOSE, 0, 0);
            }
            return TRUE;
        }, static_cast<LPARAM>(pid));

        if (WaitForSingleObject(m_process, 2000) == WAIT_TIMEOUT)
        {
            TerminateProcess(m_process, 0);
        }
        CloseHandle(m_process);
        m_process = nullptr;
    }

    wchar_t modulePath[MAX_PATH];
    GetModuleFileNameW(g_hInst, modulePath, MAX_PATH);
    fs::path dllDir = fs::path(modulePath).parent_path();

    std::wstring exePath = (dllDir / L"VectorDrawablePreviewHandler.exe").wstring();

    std::wostringstream hwndHex;
    hwndHex << std::hex << reinterpret_cast<uintptr_t>(m_hwndParent);

    std::wstring cmdLine =
        L"\"" + m_filePath + L"\" " +
        hwndHex.str() + L" " +
        std::to_wstring(m_rcParent.left) + L" " +
        std::to_wstring(m_rcParent.right) + L" " +
        std::to_wstring(m_rcParent.top) + L" " +
        std::to_wstring(m_rcParent.bottom);

    SHELLEXECUTEINFOW sei{ sizeof(sei) };
    sei.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_NO_UI;
    sei.lpFile = exePath.c_str();
    sei.lpParameters = cmdLine.c_str();
    sei.nShow = SW_SHOWDEFAULT;

    if (!ShellExecuteExW(&sei) || sei.hProcess == nullptr)
    {
        return E_FAIL;
    }

    m_process = sei.hProcess;
    m_launched = true;
    return S_OK;
}

namespace {

void PreviewLog(const std::wstring& msg)
{
    wchar_t tempPath[MAX_PATH];
    if (GetTempPathW(MAX_PATH, tempPath) == 0)
    {
        return;
    }
    std::wstring logPath = std::wstring(tempPath) + L"VectorDrawablePreviewDll.log";
    std::wofstream log(logPath, std::ios::app);
    if (log.is_open())
    {
        log << msg << L"\n";
        log.close();
    }
}

// Reads the head of `path` and returns true if its root element is <vector>.
bool IsVectorFile(const std::wstring& path)
{
    std::ifstream in(path, std::ios::binary);
    if (!in.is_open())
    {
        return false;
    }

    std::string content;
    char buffer[4096];
    while (content.size() < 4096 && in.read(buffer, sizeof(buffer)).gcount() > 0)
    {
        content.append(buffer, in.gcount());
    }
    in.close();

    size_t i = 0;
    size_t n = content.size();
    while (i < n)
    {
        size_t lt = content.find('<', i);
        if (lt == std::string::npos)
        {
            return false;
        }
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
        size_t start = lt + 1;
        size_t end = start;
        while (end < n && content[end] != ' ' && content[end] != '\t' && content[end] != '\r' && content[end] != '\n' && content[end] != '>' && content[end] != '/')
        {
            end++;
        }
        std::string tag = content.substr(start, end - start);
        for (auto& c : tag)
        {
            c = static_cast<char>(tolower(static_cast<unsigned char>(c)));
        }
        return tag == "vector";
    }
    return false;
}

} // namespace
