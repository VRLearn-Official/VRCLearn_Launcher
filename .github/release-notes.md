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

- 资源导入支持运行中的项目，覆盖已有文件前二次确认
- 还原改为弹窗确认并警示无法撤销，不再预先备份
- 修复插件包解压限制可被伪造元数据绕过
- 备份进度与还原保持一致
- 修正详情页标签与图标对齐

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

- Resource import now works with running projects and asks for confirmation before overwriting existing files
- Restore now asks for confirmation in a dialog and warns that it cannot be undone; no pre-restore backup is made
- Fixed plugin archive size limits being bypassable via forged metadata
- Backup progress updates at the same rate as restore
- Fixed alignment of tags and icons in project details

The installer is not code-signed yet, so Windows SmartScreen may warn on first run. Choose "More info" and then "Run anyway".
