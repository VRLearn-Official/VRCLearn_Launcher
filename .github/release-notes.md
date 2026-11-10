## 安装

1. 下载下方 Assets 中的 `VRCLearnLauncher-<版本>-win-x64-Setup.exe`。
2. 双击运行安装程序，按提示完成安装。

## 系统要求

- Windows 10 或更高版本，x64
- 安装包已内置 .NET 10 运行时，无需单独安装

## 校验

Release 附有 `VRCLearnLauncher-<版本>-win-x64-Setup.exe.sha256`。

```powershell
Get-FileHash .\VRCLearnLauncher-<版本>-win-x64-Setup.exe -Algorithm SHA256
```

## 本次更新

- 项目详情改为一个包列表：已安装和可安装的包放在一起，可直接添加、升级、重装、移除，也支持批量操作
- 升级包时自动升级它依赖的包；会影响其他包时先弹窗说明，可选择全部应用或只应用当前包
- 升级按钮直接显示新版本号
- 启动时发现新版本会提示，可直接打开下载页
- 长列表和页面切换更流畅
- 修复程序放着不动也会持续占用更多内存
- 停止维护部分语言包

安装包暂未签名，首次运行时 Windows SmartScreen 可能提示；选择「更多信息」→「仍要运行」。

---

## Install

1. Download `VRCLearnLauncher-<version>-win-x64-Setup.exe` from the Assets below.
2. Run the setup and follow the wizard.

## Requirements

- Windows 10 or later, x64
- The .NET 10 runtime is bundled; no separate installation is required

## Verify

The release includes `VRCLearnLauncher-<version>-win-x64-Setup.exe.sha256`.

```powershell
Get-FileHash .\VRCLearnLauncher-<version>-win-x64-Setup.exe -Algorithm SHA256
```

Or in Git Bash, WSL, Linux or macOS:

```bash
sha256sum -c VRCLearnLauncher-<version>-win-x64-Setup.exe.sha256
```

## What's new

- Project details now show all packages in one list: installed and available packages together, with add, upgrade, reinstall and remove actions plus batch operations
- Upgrading a package also upgrades its dependencies; if other packages are affected, a prompt lets you apply everything or only the current package
- Upgrade buttons show the new version number
- The launcher now prompts when a new version is available at startup and can open the download page
- Long lists and page switches are smoother
- Fixed memory usage growing while the launcher sits idle
- Some language packs are no longer maintained

The installer is not code-signed yet, so Windows SmartScreen may warn on first run. Choose "More info" and then "Run anyway".
