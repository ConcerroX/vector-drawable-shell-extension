#include "pch.h"
#include "dllmain.h"
#include "ClassFactory.h"

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, void** ppv)
{
    if (ppv == nullptr)
    {
        return E_POINTER;
    }
    *ppv = nullptr;

    FactoryKind kind;
    if (IsEqualCLSID(rclsid, CLSID_VectorDrawableThumbnailProvider))
    {
        kind = FactoryKind::Thumbnail;
    }
    else if (IsEqualCLSID(rclsid, CLSID_VectorDrawablePreviewHandler))
    {
        kind = FactoryKind::Preview;
    }
    else
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    auto* factory = new (std::nothrow) ClassFactory(kind);
    if (factory == nullptr)
    {
        return E_OUTOFMEMORY;
    }

    HRESULT hr = factory->QueryInterface(riid, ppv);
    factory->Release();
    return hr;
}

STDAPI DllCanUnloadNow()
{
    return g_cDllRef > 0 ? S_FALSE : S_OK;
}
