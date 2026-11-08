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

- 拖放导入改为全窗口自动识别：拖入项目文件夹、备份、插件或模板时显示全窗提示，松开后自动切换页面并导入；混合类型按项目、备份、插件、模板的顺序处理
- 插件页改用列表布局，操作项集中在右侧；来源文本可直接点击；过长的简介省略显示；缺少依赖时在名称旁显示警告图标和提示
- 项目详情页的备份列表支持展开与收起，默认收起并带展开动画
- 未登记在 VPM 清单中的包，徽标改为「不在仓库」，并提示通过 Unity 的包管理器管理
- 移除侧栏收起与锁定功能，侧栏保持常驻
- 长列表启用项级虚拟化并限制页面视图缓存数量，插件图标按显示尺寸解码，插件卸载的垃圾回收移至后台线程，备份搜索加入防抖，降低内存占用与界面卡顿
- 安装或卸载时若检测到启动器正在运行，会提示并可在确认后自动关闭后再继续
- 修复拖放导入期间偶发的列表刷新报错

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

- Drag and drop now works anywhere in the window: dropping project folders, backups, plugins or templates shows a full-window hint, switches to the matching page and imports automatically; mixed drops are processed in the order projects, backups, plugins, templates
- The plugins page now uses a list layout with actions on the right; sources are clickable; long descriptions are truncated; missing dependencies are indicated by a warning icon next to the name
- The backup list in project details can be expanded and collapsed, and is collapsed by default
- Packages not registered in the VPM manifest now show a “Not in repository” badge and a hint to manage them through Unity’s Package Manager
- Removed the sidebar collapse and lock features; the sidebar is always visible
- Long lists now use item virtualization and a bounded page-view cache; plugin icons are decoded at display size; plugin unload garbage collection runs off the UI thread; backup search is debounced. This reduces memory use and UI stalls
- The installer now detects a running launcher and can close it automatically after confirmation before installing or uninstalling
- Fixed occasional list-refresh errors during drag-and-drop imports

The installer is not code-signed yet, so Windows SmartScreen may warn on first run. Choose "More info" and then "Run anyway".
