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

- 配置与数据迁移到独立目录 `%LOCALAPPDATA%\VRCLearnLauncher`，不再与 VCC 共用，VCC 重写 settings.json 不会再覆盖本程序配置
- 首次启动自动迁移旧配置，项目、仓库、编辑器、插件与资源数据保持不变
- 包缓存与项目备份文件保留在原位置，可在设置中使用现有的迁移功能搬移

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

- Configuration and data moved to `%LOCALAPPDATA%\VRCLearnLauncher`, independent of VCC; VCC rewriting settings.json no longer overwrites this app's configuration
- The first launch migrates the old configuration automatically; projects, repositories, editors, plugins and resources are preserved
- Package caches and project backups stay where they are and can be moved with the existing migration tools in Settings

The installer is not code-signed yet, so Windows SmartScreen may warn on first run. Choose "More info" and then "Run anyway".
