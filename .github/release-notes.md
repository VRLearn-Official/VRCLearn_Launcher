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

- 更新正在运行的插件后需重启程序才能再次打开，不再尝试热重载
- 设置页精简说明文字，新增「重新检测」「打开目录」按钮，各页刷新按钮统一
- 移除窗口的 Mica 背景效果
- 日志文件名前缀改为 vcl
- 修复包内文件名含冒号时会写入 NTFS 备用数据流
- 包解压拒绝重复条目，避免静默覆盖
- 修复项目还原日志损坏时启动后没有主窗口
- 启动时的还原检查不再阻塞界面线程
- 修复下载响应未及时释放
- 修复取消下载被当作网络失败
- 修复复制项目时进度到不了总数
- 修复包目录名大小写与清单不一致时包无法操作
- 设置页输入代理地址等不再逐字符写盘
- 保存设置不再重扫 Unity 编辑器
- 修复配置中的异常路径可能操作数据目录外的文件
- 语言包占位符写错时不再中断相关流程
- 安装包升级时不再清空安装目录
- 移除更新地址的环境变量覆盖
- 清理无用代码、统一界面服务的注入方式

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

- Updated plugins now require a restart before they can be opened again; hot reload has been removed
- Simplified hints on the settings page, added "Recheck" and "Open folder" buttons, and unified refresh buttons across pages
- Removed the window's Mica backdrop
- Log file names now use the vcl prefix
- Fixed archive entries containing a colon being written as NTFS alternate data streams
- Extraction now rejects duplicate entries instead of silently overwriting
- Fixed a missing main window at startup when the project restore journal is corrupted
- The startup restore check no longer blocks the UI thread
- Fixed download responses not being released promptly
- Fixed a cancelled download being reported as a network failure
- Fixed copy progress never reaching the total
- Fixed packages being unmanageable when the package directory case differs from the manifest
- Settings no longer write to disk on every keystroke when typing a proxy address
- Saving settings no longer rescans Unity editors
- Fixed abnormal paths in the configuration being able to touch files outside the data directory
- Malformed placeholders in language packs no longer interrupt the related flow
- Upgrading via the installer no longer clears the installation directory
- Removed the environment variable overrides for update URLs
- Removed unused code and unified how UI services are injected

The installer is not code-signed yet, so Windows SmartScreen may warn on first run. Choose "More info" and then "Run anyway".
