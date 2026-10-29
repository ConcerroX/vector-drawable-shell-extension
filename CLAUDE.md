# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Windows Shell extension that lets File Explorer show **Android VectorDrawable** XML files
(`*.xml` whose root element is `<vector>`) as thumbnails and in the preview pane.

The architecture deliberately mirrors PowerToys' SVG preview module (studied from
`microsoft/PowerToys`): a native C++ COM DLL exposes `IThumbnailProvider` / `IPreviewHandler`
and delegates the actual rendering to a .NET EXE that rasterizes via **WebView2 (Chromium)**.

## Build

Toolchain assumed present: Visual Studio 2022+/18 (MSVC), CMake, .NET SDK 8+.

```bash
# 1. .NET converter + EXEs (builds and publishes into deploy/)
dotnet publish src/VectorDrawableThumbnailProvider/VectorDrawableThumbnailProvider.csproj -c Release -o deploy
dotnet publish src/VectorDrawablePreviewHandler/VectorDrawablePreviewHandler.csproj  -c Release -o deploy

# 2. Native COM DLL (CMake + MSVC; needs the VS cmake.exe on PATH)
cmake -S src/VectorDrawableShellExt -B src/VectorDrawableShellExt/build -G "Visual Studio 18 2026" -A x64
cmake --build src/VectorDrawableShellExt/build --config Release
cp src/VectorDrawableShellExt/build/Release/VectorDrawableShellExt.dll deploy/

# 3. WebView2 native loader (required at deploy/ root)
cp deploy/runtimes/win-x64/native/WebView2Loader.dll deploy/
```

## Register / unregister (per-user, no admin)

```bash
powershell -ExecutionPolicy Bypass -File install.ps1      # writes HKCU + SHChangeNotify
powershell -ExecutionPolicy Bypass -File uninstall.ps1
```

`register.reg.template` is the manual `.reg` form (per-user, `HKCU\Software\Classes\...`);
`install.ps1` writes the same entries directly via PowerShell (avoids `reg.exe` path/encoding
issues) and does **not** kill Explorer — it sends `SHChangeNotify(SHCNE_ASSOCCHANGED)`.

## Architecture

```
Explorer  ──►  VectorDrawableShellExt.dll  (native C++, COM)
                    │  IThumbnailProvider   (reads stream → temp .xml → spawns EXE → loads .bmp)
                    │  IPreviewHandler      (spawns EXE with parent HWND + rect)
                    ▼
               VectorDrawableThumbnailProvider.exe  /  VectorDrawablePreviewHandler.exe
                    │  convert VectorDrawable XML → SVG, then render via WebView2
                    ▼
               VectorDrawableConverter.dll  (shared)
```

- **`src/VectorDrawableConverter/`** — the core: converts VectorDrawable XML to SVG.
  `PathData.cs` (Android `pathData` ↔ SVG `d`), `ColorResolver.cs` (`#AARRGGBB` etc. + the
  `@android:color/white`/`?attr/...` resource-ref mappings), `VectorDrawableToSvgConverter.cs`
  (the `<vector>`/`<group>`/`<path>`/`<clip-path>` walk). The attribute mapping follows AOSP's
  `tools/base/sdk-common/.../vectordrawable/` (VdPath/VdGroup/PathParser/VdUtil) — that is the
  authoritative source for how Android renders these files.
- **`src/VectorDrawableThumbnailProvider/`** — WinExe. Args: `<file> <size>`; writes
  `<file>.png` (extension replaced, not appended — see note below).
- **`src/VectorDrawablePreviewHandler/`** — WinExe. Args: `<file> <hwndHex> <l> <r> <t> <b>`;
  hosts a borderless WinForms form parented into the Explorer-provided HWND.
- **`src/VectorDrawableShellExt/`** — native COM DLL. `exports.cpp` (`DllGetClassObject`/
  `DllCanUnloadNow`), `ThumbnailProvider.cpp`, `PreviewHandler.cpp`, `ClassFactory.cpp`.
  CLSIDs and the `IInitializeWithStream`/`IInitializeWithFile`/`IThumbnailProvider`/
  `IPreviewHandler` interfaces. `VectorDrawableShellExt.def` exports the two entry points.

## Non-obvious / gotchas

- **Content sniffing is mandatory.** Registering a handler for `.xml` means it is invoked for
  *every* XML file. The COM DLL (and the EXEs defensively) must read the file head and return
  `E_FAIL` when the root element is not `<vector>`, so Explorer falls back to the default `.xml`
  handler. See `ContainsVectorRoot`/`IsVectorFile`.
- **PNG filename contract + alpha preservation.** The thumbnail EXE writes `<temp>\{GUID}.png`
  (32bpp ARGB). The DLL loads it with **GDI+** and blits it onto a **`CreateDIBSection`**
  (32bpp, top-down `biHeight=-h`) so the per-pixel alpha survives. Two classic traps:
  - `LoadImageW` cannot load PNG (only BMP/ICO/CUR) — returns null.
  - `GDI+ Bitmap::GetHBITMAP` **loses alpha** (produces a DDB, not a DIB section) — the
    transparent background turns black. This was the root cause of "all icons black".
  - All GDI+ objects must be destructed **before** `GdiplusShutdown` (scope them in a block),
    otherwise the process crashes on exit.
- **Tint is applied by color replacement, not an SVG filter.** Android's `android:tint` is a
  PorterDuff SRC_IN over the whole drawable. An `feFlood`/`feComposite` filter reproduces this
  but Chromium's `CapturePreviewAsync` flattens transparent regions to black, so instead the
  converter rewrites each path's fill/stroke to the tint color directly. This is correct for the
  monochrome icons this extension targets.
- **Android colors are `#AARRGGBB` (alpha first);** SVG/CSS are `#RRGGBB` + `fill-opacity`.
  `ColorResolver` splits alpha out. `#RGB`/`#ARGB` shorthand is expanded.
- **`pathData` == SVG `d`**, with one difference: Android's parser accepts arc flags as
  `1.0`/`0.0` (floats), SVG requires bare `1`/`0`. `PathData.cs` normalizes this.
- **Group transform order** is `T(-pivot)·S·R·T(t+pivot)` (Android's `VGroup::updateLocalMatrix`),
  emitted as SVG `transform="translate(tx+px,ty+py) rotate(deg) scale(sx,sy) translate(-px,-py)"`.
- **`DEFINE_GUID` needs `#include <initguid.h>` in exactly one TU** (here `dllmain.cpp`) or the
  CLSID symbols are extern-only and fail to link.
- **The `Microsoft.Web.WebView2` NuGet ships a WPF assembly** that triggers a harmless
  `MSB3277` WindowsBase version-conflict warning on WinForms projects. Ignore it.
- **WebView2 user-data folder** must be per-install (see `_userDataFolder` in the two EXEs) and
  the environment options must disable scripts/dialogs/devtools (matching PowerToys' hardening).
- **`SetCurrentDirectory(AppContext.BaseDirectory)`** (from PowerToys) resets the EXE CWD to its
  own install dir, which breaks relative-path arguments. Always pass **absolute paths** to the
  EXEs from the DLL (it already uses `GetTempPathW` + absolute `dllDir`).

## Tests / verification

- `src/VectorDrawableConverter.Tests/` — CLI that runs the converter on a file and prints/dumps
  the SVG. Fixtures live in `tests/fixtures/` (copied from the ColorOS theme project's real
  VectorDrawable XML files).
- `tools/E2eTest/` — CoCreates the registered thumbnail provider and drives
  `IInitializeWithStream → GetThumbnail` end-to-end through the real registry path.
- `tools/PreviewE2eTest/` — same for `IPreviewHandler` (SetWindow → DoPreview).
