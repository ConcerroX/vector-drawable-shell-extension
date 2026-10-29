#pragma once

#include "pch.h"

#include <shobjidl.h>
#include <ocidl.h>

// IPreviewHandler that spawns VectorDrawablePreviewHandler.exe with the Explorer preview pane
// HWND and rectangle. Mirrors PowerToys' out-of-process preview handler model.
class PreviewHandler final
    : public IPreviewHandler
    , public IInitializeWithFile
    , public IObjectWithSite
{
public:
    PreviewHandler();
    ~PreviewHandler();

    // IUnknown
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    IFACEMETHODIMP_(ULONG) AddRef() override;
    IFACEMETHODIMP_(ULONG) Release() override;

    // IInitializeWithFile
    IFACEMETHODIMP Initialize(LPCWSTR pszFilePath, DWORD grfMode) override;

    // IObjectWithSite
    IFACEMETHODIMP SetSite(IUnknown* pUnkSite) override;
    IFACEMETHODIMP GetSite(REFIID riid, void** ppv) override;

    // IPreviewHandler
    IFACEMETHODIMP SetWindow(HWND hwnd, const RECT* prc) override;
    IFACEMETHODIMP SetRect(const RECT* prc) override;
    IFACEMETHODIMP DoPreview() override;
    IFACEMETHODIMP Unload() override;
    IFACEMETHODIMP SetFocus() override;
    IFACEMETHODIMP QueryFocus(HWND* phwnd) override;
    IFACEMETHODIMP TranslateAccelerator(MSG* pmsg) override;

private:
    HRESULT Launch();

    long m_cRef;
    std::wstring m_filePath;
    HWND m_hwndParent;
    RECT m_rcParent;
    IUnknown* m_punkSite;
    HANDLE m_process;
    bool m_launched;
};
