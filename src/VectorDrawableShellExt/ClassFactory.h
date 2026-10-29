#pragma once

#include "pch.h"

#include <unknwn.h>

// CLSID this factory creates.
enum class FactoryKind { Thumbnail, Preview };

// Minimal IClassFactory that instantiates either a ThumbnailProvider or a PreviewHandler.
class ClassFactory final : public IClassFactory
{
public:
    ClassFactory(FactoryKind kind);
    ~ClassFactory();

    // IUnknown
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    IFACEMETHODIMP_(ULONG) AddRef() override;
    IFACEMETHODIMP_(ULONG) Release() override;

    // IClassFactory
    IFACEMETHODIMP CreateInstance(IUnknown* pUnkOuter, REFIID riid, void** ppv) override;
    IFACEMETHODIMP LockServer(BOOL fLock) override;

private:
    long m_cRef;
    FactoryKind m_kind;
};
