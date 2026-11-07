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

- 还原备份时自动识别压缩包内的 Unity 项目根目录，兼容外层多套一层或多层文件夹
- 找不到 ProjectSettings/ProjectVersion.txt 或有多个候选项目根时拒绝还原，原项目不受影响
- 修正备份页还原后未区分是否加入项目列表的提示

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

- Backups are now flattened automatically: the Unity project root is detected inside archives with one or more wrapper folders
- Restores are refused when ProjectSettings/ProjectVersion.txt is missing or multiple project roots are found; the existing project is left untouched
- The backup page now distinguishes whether a restored folder was actually added to the project list

The installer is not code-signed yet, so Windows SmartScreen may warn on first run. Choose "More info" and then "Run anyway".
