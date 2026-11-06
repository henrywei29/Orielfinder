# Orielfolder

Orielfolder is the public Windows desktop distribution and source repository for the **Orielfolder** file manager. It contains the WinUI 3 desktop application, background thumbnail and document preview workers, Listary 6 integration adapter, automated verification suite, packaging scripts, and prebuilt Windows x64 setup installer.

End users do not need the build toolchain to install Orielfolder. Use the prebuilt setup executable under [`dist/`](dist/) to install the self-contained Windows x64 application directly.

---

Orielfolder 是面向 Windows 10/11 的现代桌面文件管理器开源仓库与发行包。仓库包含完整的 WinUI 3 桌面主程序、缩略图与 Office/PDF 文档预览工作进程、Listary 6 集成插件、自动化测试脚本以及预构建的 Windows x64 安装程序。

普通用户无需配置开发环境或从源码编译，可直接使用 [`dist/`](dist/) 目录下的预构建安装包 [`OrielFolder-1.0.1-x64-Setup.exe`](dist/OrielFolder-1.0.1-x64-Setup.exe) 完成安装。

## Repository contents

| Path | Description |
| :--- | :--- |
| [`dist/`](dist/) | Prebuilt Windows x64 setup installer (`OrielFolder-1.0.1-x64-Setup.exe`) and installation notes (`install-notice.txt`) / 预构建 Windows x64 安装程序与安装说明 |
| [`src/`](src/) | Solution source code (`OrielFolder` main app, `OrielFolder.Thumbs` worker, `OrielFolder.DocumentPreview.Worker`, `Listary.FileAppPlugin.OrielFolder`, `OrielFolder.Tests`, `OrielFolder.UITests`) / 核心源码与测试工程 |
| [`installer/`](installer/) | Inno Setup script (`OrielFolder.iss`) and PowerShell setup helpers (`Install-OrielFolder.ps1`, `Install-OrielFolder-Machine.ps1`) / 安装向导配置与脚本 |
| [`tests/`](tests/) | Integration, UI layout, preview, and regression verification scripts / 集成测试与回归验证脚本 |
| [`third_party/`](third_party/) | Vendored native archive engine (`otterzip_ffi.dll`) and integrity checks / 原生压缩包引擎组件 |
| [`package-oriel.ps1`](package-oriel.ps1) | End-to-end distribution builder (MSIX packaging, signing, and `.exe` setup compilation) / 一键打包与签名脚本 |
| [`build-dev.ps1`](build-dev.ps1) | Local development build entry script / 本地开发构建脚本 |
| [`start-dev.ps1`](start-dev.ps1) | Local development package registration and launch script / 本地开发启动脚本 |
| [`LICENSE`](LICENSE) / [`LICENSE.md`](LICENSE.md) | GPL-3.0 license and fork notice / 开源许可证与分支声明 |

## Quick installation (Prebuilt `.exe`)

1. Download the `dist/` folder (`OrielFolder-1.0.1-x64-Setup.exe` together with `OrielFolder-1.0.1-x64-Setup-1.bin` ~ `-3.bin` in the same directory) or the single-file installer from [GitHub Releases](https://github.com/henrywei29/Orielfinder/releases).
2. Double-click `OrielFolder-1.0.1-x64-Setup.exe` on Windows 10 (version 2004 / build 19041) or Windows 11 (x64).
3. Review the installation notice, check **I agree to trust this app certificate / 我同意安装此应用的签名证书** (adds the self-signed public certificate to Windows `Trusted People` for MSIX package verification), and optionally enable the desktop shortcut or **Listary 6 integration**.
4. Launch **Orielfolder** from the Start Menu or desktop shortcut.

> **Note**: The main application bundles self-contained .NET 8 and Windows App SDK runtimes. Office document preview requires a local Microsoft Office installation; PDF/Web preview requires Microsoft Edge WebView2 Runtime.

## Key capabilities

- **Miller Columns & Inline Tree View**: Fast hierarchical column navigation (`Ctrl+1`) alongside Details (`Ctrl+2`), List (`Ctrl+3`), and Icon (`Ctrl+4`) views with inline folder tree expansion.
- **Multi-Pane Split Workspace**: Dual-pane and quad-pane workspace layouts with independent tabs, drag-and-drop file operations, and a floating **Shelf** for staging files across directories.
- **Document Preview, Quick Look & OCR**: Built-in preview panel and `Space` Quick Look window supporting images, PDFs (with annotations & export), Office documents (Word/Excel/PowerPoint), syntax-highlighted source code, archives, and offline image OCR.
- **Folder Tags & Color Labels**: Persistent color tags and custom label categories without polluting user folders with `desktop.ini` files.
- **Listary 6 Integration**: Dedicated `Listary.FileAppPlugin.OrielFolder` bridge for instant folder switching and file dialog integration.
- **Configuration Backup & Product Icons**: Export/import full user settings and switch between **Blue** and **Classic** application icon themes.

## Build from source

### Prerequisites

- Windows 10 SDK (10.0.19041.0 or later)
- .NET 8 SDK (`8.0.406` or compatible, configured via [`global.json`](global.json))
- Inno Setup 6/7 (for compiling the `.exe` installer via `package-oriel.ps1`)

### Commands

```powershell
# Build the development application (x64)
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-dev.ps1

# Build the Listary 6 plugin
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-listary-plugin.ps1

# Verify OrielFolder identity isolation
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\test-oriel-profile.ps1

# Build full MSIX + signed Setup.exe installer
powershell -NoProfile -ExecutionPolicy Bypass -File .\package-oriel.ps1
```

## License

Orielfolder is distributed under the [GNU General Public License v3.0](LICENSE). See [LICENSE.md](LICENSE.md) and [OpenSourceLicenses.md](OpenSourceLicenses.md) for third-party notices and the UnRAR exception.
