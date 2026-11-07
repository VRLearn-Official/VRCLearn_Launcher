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

- 修复插件异步释放时退出可能卡住
- 修复预发布版本范围匹配与 semver 不一致
- 修复项目恢复中断后原目录可能无法找回
- 修复仓库或设置写盘失败时仍提示成功
- 修复日志写入失败影响其他功能
- 修复缓存目录含无权访问的子目录时清理或统计失败
- 修复启动 Unity 超时后仍可能在后台继续启动
- 修复项目移除后自动备份倒计时与变更检测状态残留
- 修复详情页启动 Unity 时返回列表可能静默失败
- 修复仓库请求包含空 header 值导致认证失败

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

- Fixed the app hanging on exit when a plugin releases asynchronously
- Fixed prerelease version ranges not matching per semver
- Fixed the original folder becoming unrecoverable after an interrupted project restore
- Fixed save failures for repositories or settings being reported as success
- Fixed log write failures affecting other features
- Fixed cache cleanup and size calculation failing on inaccessible subfolders
- Fixed Unity possibly continuing to launch in the background after a timeout
- Fixed stale auto-backup countdown and change-detection state after a project was removed
- Fixed a silent failure when returning to the project list while Unity is launching
- Fixed repository requests failing authentication due to empty header values

The installer is not code-signed yet, so Windows SmartScreen may warn on first run. Choose "More info" and then "Run anyway".
