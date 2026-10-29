#pragma once

#include "pch.h"

#include <shobjidl.h>

// IThumbnailProvider that delegates rendering to VectorDrawableThumbnailProvider.exe.
// The stream is written to a temp .xml file, the EXE is spawned (blocking), and the
// resulting .bmp is loaded. This mirrors PowerToys' SVG thumbnail provider.
class ThumbnailProvider final
    : public IThumbnailProvider
    , public IInitializeWithStream
{
public:
    ThumbnailProvider();
    ~ThumbnailProvider();

    // IUnknown
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    IFACEMETHODIMP_(ULONG) AddRef() override;
    IFACEMETHODIMP_(ULONG) Release() override;

    // IInitializeWithStream
    IFACEMETHODIMP Initialize(IStream* pStream, DWORD grfMode) override;

    // IThumbnailProvider
    IFACEMETHODIMP GetThumbnail(UINT cx, HBITMAP* phbmp, WTS_ALPHATYPE* pdwAlpha) override;

private:
    long m_cRef;
    IStream* m_pStream;
};
