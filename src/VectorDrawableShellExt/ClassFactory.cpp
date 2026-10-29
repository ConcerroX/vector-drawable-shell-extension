#include "pch.h"
#include "ClassFactory.h"
#include "ThumbnailProvider.h"
#include "PreviewHandler.h"

#include <new>

ClassFactory::ClassFactory(FactoryKind kind) : m_cRef(1), m_kind(kind)
{
    InterlockedIncrement(&g_cDllRef);
}

ClassFactory::~ClassFactory()
{
    InterlockedDecrement(&g_cDllRef);
}

IFACEMETHODIMP ClassFactory::QueryInterface(REFIID riid, void** ppv)
{
    static const QITAB qit[] = {
        QITABENT(ClassFactory, IClassFactory),
        { nullptr },
    };
    return QISearch(this, qit, riid, ppv);
}

IFACEMETHODIMP_(ULONG) ClassFactory::AddRef()
{
    return InterlockedIncrement(&m_cRef);
}

IFACEMETHODIMP_(ULONG) ClassFactory::Release()
{
    ULONG cRef = InterlockedDecrement(&m_cRef);
    if (cRef == 0)
    {
        delete this;
    }
    return cRef;
}

IFACEMETHODIMP ClassFactory::CreateInstance(IUnknown* pUnkOuter, REFIID riid, void** ppv)
{
    if (pUnkOuter != nullptr)
    {
        return CLASS_E_NOAGGREGATION;
    }

    HRESULT hr = E_OUTOFMEMORY;

    if (m_kind == FactoryKind::Thumbnail)
    {
        auto* p = new (std::nothrow) ThumbnailProvider();
        if (p)
        {
            hr = p->QueryInterface(riid, ppv);
            p->Release();
        }
    }
    else
    {
        auto* p = new (std::nothrow) PreviewHandler();
        if (p)
        {
            hr = p->QueryInterface(riid, ppv);
            p->Release();
        }
    }

    return hr;
}

IFACEMETHODIMP ClassFactory::LockServer(BOOL fLock)
{
    if (fLock)
    {
        InterlockedIncrement(&g_cDllRef);
    }
    else
    {
        InterlockedDecrement(&g_cDllRef);
    }
    return S_OK;
}
