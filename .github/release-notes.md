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

- 应用包变更支持取消，进度显示当前处理的包
- 应用包变更的进度不显示操作提示
- 修复详情页包名过长不省略
- 清单不一致时提示列出需手动安装的包
- 修复复制项目时目标路径为空会复制进所选文件夹
- 修复配置备份遇外部删除文件时整次失败
- 修复读取程序版本信息时内存未固定
- 清理无用代码与冗余兜底

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

- Package changes can now be cancelled, with the current package shown in the progress view
- The progress view for package changes no longer shows the hint
- Fixed long package names not being ellipsized in project details
- Added the packages that need manual installation when the manifest does not match installed packages
- Fixed copying a project into the selected folder when the target path is empty
- Fixed configuration backup failing entirely when an externally deleted file is encountered
- Fixed memory not being pinned while reading executable version information
- Removed unused code and redundant fallbacks

The installer is not code-signed yet, so Windows SmartScreen may warn on first run. Choose "More info" and then "Run anyway".
