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

- 备份新增默认勾选的「着色器缓存」，并跳过 Unity 占用的缓存文件
- 移除归档、实验性功能与自动/全量备份
- 修复新建项目缺少 Unity 包清单导致 VRChat SDK 测试程序集编译失败
- 插件有更新时「打开」改为「更新」
- 统一刷新按钮内的进度提示，修复备份时误显示
- 仓库页调整按钮顺序，「导入仓库」改为「导入」

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

- Backups can now include the shader cache (enabled by default); files locked by Unity are skipped
- Removed archives, experimental features, and automatic/full backups
- Fixed new projects missing the Unity package manifest, which broke compilation of the VRChat SDK test assemblies
- Plugins with an update now show "Update" instead of "Open"
- Unified the progress indicator inside refresh buttons and fixed it appearing during backups
- Reordered the repository page buttons and renamed "Import repos" to "Import"

The installer is not code-signed yet, so Windows SmartScreen may warn on first run. Choose "More info" and then "Run anyway".
