# Vector Drawable Shell Extension

在 Windows 文件资源管理器中预览 Android **Vector Drawable** XML 文件（根元素为 `<vector>` 的 `*.xml`），支持：

- **缩略图**（`IThumbnailProvider`）—— 文件列表/图标/缩略图视图
- **预览窗格**（`IPreviewHandler`）—— 右侧预览窗格大图

架构模仿 PowerToys 的 SVG 预览模块：原生 C++ COM DLL 暴露 shell 接口，实际渲染交给 .NET EXE，用 **WebView2 (Chromium)** 把 VectorDrawable 转成 SVG 后栅格化。

## 构建

依赖：Visual Studio 2022+/18（MSVC）、CMake、.NET SDK 8+。

```bash
# 1. .NET 组件（转换器 + 两个 EXE，发布到 deploy/）
dotnet publish src/VectorDrawableThumbnailProvider/VectorDrawableThumbnailProvider.csproj -c Release -o deploy
dotnet publish src/VectorDrawablePreviewHandler/VectorDrawablePreviewHandler.csproj  -c Release -o deploy

# 2. 原生 COM DLL
cmake -S src/VectorDrawableShellExt -B src/VectorDrawableShellExt/build -G "Visual Studio 18 2026" -A x64
cmake --build src/VectorDrawableShellExt/build --config Release
cp src/VectorDrawableShellExt/build/Release/VectorDrawableShellExt.dll deploy/

# 3. WebView2 原生加载器
cp deploy/runtimes/win-x64/native/WebView2Loader.dll deploy/
```

## 安装 / 卸载（per-user，无需管理员）

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1     # 写 HKCU + 通知 Explorer
powershell -ExecutionPolicy Bypass -File uninstall.ps1
```

注册为 per-user（`HKCU\Software\Classes\...`），与 PowerToys 默认一致。`install.ps1` 只发
`SHChangeNotify` 刷新，不重启 Explorer。

## 工作原理

1. Explorer 调用 `VectorDrawableShellExt.dll`（原生 COM）
2. DLL 读取文件流/路径，**内容嗅探**确认根元素是 `<vector>`，否则返回失败让默认 `.xml` 处理器接管
3. DLL 把内容写到临时文件，spawn 对应的 .NET EXE
4. EXE 用 `VectorDrawableConverter` 把 VectorDrawable XML 转成 SVG，交给 WebView2 渲染
5. 缩略图 EXE 输出 BMP 由 DLL 加载；预览 EXE 把窗体嵌入 Explorer 预览窗格

## 目录

- `src/VectorDrawableConverter/` — 核心转换器（vector XML → SVG）
- `src/VectorDrawableThumbnailProvider/` — 缩略图 EXE
- `src/VectorDrawablePreviewHandler/` — 预览窗格 EXE
- `src/VectorDrawableShellExt/` — 原生 COM DLL
- `tools/E2eTest/`、`tools/PreviewE2eTest/` — 端到端验证
- `tests/fixtures/` — 真实 VectorDrawable 测试文件

## 转换规则（源自 AOSP `tools/base` vectordrawable 包）

| VectorDrawable | SVG |
|---|---|
| `android:pathData` | `d`（arc 标志位 `1.0`→`1`） |
| `android:fillColor` | `fill`（`#AARRGGBB`→`#RRGGBB` + `fill-opacity`） |
| `android:strokeColor` / `strokeWidth` | `stroke` / `stroke-width` |
| `android:fillType="evenOdd"` | `fill-rule="evenodd"` |
| `android:strokeLineCap/Join` | `stroke-linecap` / `stroke-linejoin` |
| `android:tint` | 直接替换 fill/stroke 颜色 |
| `<group>` 变换 | `transform="translate rotate scale translate"` |
