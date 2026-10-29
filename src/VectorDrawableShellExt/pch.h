#pragma once

// Target Windows 10+ (thumbcache/WebView2-era APIs).
#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0A00
#endif
#ifndef WINVER
#define WINVER 0x0A00
#endif

#define WIN32_LEAN_AND_MEAN
#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <windows.h>
#include <objbase.h>
#include <shobjidl.h>   // IPreviewHandler, IPreviewHandlerFrame
#include <thumbcache.h> // IThumbnailProvider, WTS_ALPHATYPE
#include <propsys.h>    // IInitializeWithFile, IInitializeWithStream
#include <ocidl.h>      // IObjectWithSite
#include <shlwapi.h>
#include <gdiplus.h>
#include <string>
#include <memory>
#include <cstdint>

// Shared globals defined in dllmain.cpp.
extern HINSTANCE g_hInst;
extern long g_cDllRef;
