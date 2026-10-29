#pragma once

#include "pch.h"

// GUIDs shared with the .NET EXEs and the registration script.
// VectorDrawable thumbnail provider CLSID.
// {2D063192-2936-47B0-9029-EEC429B56CAD}
DEFINE_GUID(CLSID_VectorDrawableThumbnailProvider,
    0x2d063192, 0x2936, 0x47b0, 0x90, 0x29, 0xee, 0xc4, 0x29, 0xb5, 0x6c, 0xad);

// VectorDrawable preview handler CLSID.
// {7DE97D1F-66CA-415B-9C8C-4D4D57504687}
DEFINE_GUID(CLSID_VectorDrawablePreviewHandler,
    0x7de97d1f, 0x66ca, 0x415b, 0x9c, 0x8c, 0x4d, 0x4d, 0x57, 0x50, 0x46, 0x87);

// IPreviewHandler IID (shobjidl.h defines it; here for clarity).
// {8895b1c6-b41f-4c1c-a562-0d564250836f}

extern HINSTANCE g_hInst;
extern long g_cDllRef;
