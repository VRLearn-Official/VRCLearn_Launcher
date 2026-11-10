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

- 项目详情页统一为「管理所有的包」列表，已安装与可安装的包在同一处展示，支持直接添加、升级、重装、移除，以及批量安装、批量升级、全部升级与清空选择；确认后直接应用，不再保留待应用清单
- 升级包时同步升级其依赖；当目标版本会影响其他包时先提示兼容性，可选择全部应用或仅应用当前包
- 升级提示改为带目标版本号的按钮，可直接发起升级
- 启动时检查新版本，发现更新后提示并可直接打开下载页
- 项目、备份、插件、资源、仓库与日志页的列表改为虚拟化或后台读取，切换页面与刷新的卡顿减少
- 项目详情首次打开时，仓库解析与 Unity 编辑器扫描移至后台；备份列表改用虚拟化模板
- 修复程序空闲时内存持续上涨
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

- Project details now use a single “All packages” list that shows installed and available packages together, with direct add, upgrade, reinstall and remove actions plus batch install, batch upgrade, upgrade all and clear selection; changes are applied immediately after confirmation, so the pending-changes list is gone
- Upgrading a package now upgrades its dependencies as well; when the target version affects other packages, a compatibility prompt lets you apply everything or only the current package
- Upgrade hints are now buttons showing the target version and start the upgrade directly
- The launcher checks for updates at startup and, when one is available, offers to open the download page
- Long lists on the projects, backups, plugins, resources, repositories and logs pages now use virtualization or background loading, reducing stalls when switching pages and refreshing
- Opening project details now parses repositories and scans Unity editors in the background; the backup list uses a virtualized template
- Fixed memory usage growing while the launcher is idle
- Some language packs are no longer maintained

The installer is not code-signed yet, so Windows SmartScreen may warn on first run. Choose "More info" and then "Run anyway".
