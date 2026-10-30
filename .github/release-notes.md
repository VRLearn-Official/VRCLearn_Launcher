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

- 优化拖动仓库列表时的视觉效果和操作逻辑
- 项目页支持项目名搜索
- 修复全量备份遗漏嵌套目录并误包含 .git 的问题
- 修复模板依赖范围非法时新建项目崩溃
- 修复插件重装后可能被旧的待删除记录删除
- 更新插件期间不再允许关闭进度窗口，避免更新中断且无提示

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

- Improved the visuals and interaction when reordering repositories
- Projects can now be searched by name
- Full backups no longer skip nested folders or include `.git`
- Creating a project from a template with an invalid dependency range no longer crashes
- Reinstalling a plugin now clears a stale pending removal
- The update progress dialog can no longer be closed while an update is running

The installer is not code-signed yet, so Windows SmartScreen may warn on first run. Choose "More info" and then "Run anyway".
