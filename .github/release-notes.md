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

- 启动时自动刷新所有仓库，失败时提示
- 刷新仓库期间在侧栏底部显示刷新状态
- 仓库刷新日志记录每个仓库的地址、结果与失败原因
- 项目详情的批量操作按钮在窄宽度下收进「更多」菜单
- 移除项目详情的备份列表
- 「批量操作」改名为「管理包」，移除「管理所有的包」标题
- 复制项目改为独立弹窗

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

- Repositories now refresh automatically on startup; failures are reported
- Repository refresh status is shown at the bottom of the sidebar
- Repository refresh logs now include each repository's URL, result, and failure reason
- Bulk action buttons in project details move into a More menu at narrow widths
- Removed the backup list from project details
- Renamed "Bulk actions" to "Manage packages" and removed the "Manage all packages" heading
- Copy project now opens in a separate dialog

The installer is not code-signed yet, so Windows SmartScreen may warn on first run. Choose "More info" and then "Run anyway".
