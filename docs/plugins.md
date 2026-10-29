# 插件开发规范

VRCLearn Launcher 的【插件】页由本地 `.vclplugin` 包扩展。每个插件提供独立窗口，由用户安装、启用和打开。

## 信任模型

插件是**进程内 .NET DLL**，与应用权限相同：

- 可以读写任意文件、访问网络、执行任意代码，也可以拖垮主程序。
- 宿主不提供沙箱、权限声明或签名校验，只做包结构与完整性校验。
- 只安装可信插件；第三方插件的行为由作者负责。
- `IPluginContext` 是 API 约定，不是安全边界。宿主只能隔离 `Initialize` / `CreateView` 阶段的异常。

## 包格式

`.vclplugin` 是 ZIP，根目录必须有且只有一份 `plugin.json`：

```json
{
  "schemaVersion": 1,
  "id": "acme.hello",
  "name": { "zh-Hans": "示例插件", "en": "Hello plugin" },
  "description": {
    "zh-Hans": "一个最小插件。",
    "en": "A minimal plugin."
  },
  "version": "1.0.0",
  "author": "https://github.com/acme/hello",
  "apiVersion": 1,
  "minHostVersion": "1.28.0",
  "entry": {
    "assembly": "Acme.Hello.dll",
    "type": "Acme.Hello.HelloPlugin"
  },
  "icon": "icon.png",
  "window": { "width": 640, "height": 480, "resizable": true },
  "provides": [],
  "requires": []
}
```

| 字段 | 说明 |
|---|---|
| `schemaVersion` | 固定为 `1` |
| `id` | 小写字母/数字/`.`/`-`/`_`，最长 64 |
| `name` / `description` | 字符串或 `{ "语言代码": "文本" }`；缺当前语言时先回退中性语言，再回退 `en`，最后取第一个值 |
| `version` | `major.minor.patch`，用于展示和替换 |
| `author` | 插件来源的 GitHub 链接，最长 128；缺失或无法解析时界面显示「未知」 |
| `apiVersion` | 必须等于宿主 `PluginApi.Version`（当前 `1`）；目标框架升级不影响这个值 |
| `minHostVersion` | 可选；宿主版本低于它的包会被拒绝。以 `net10.0-windows` 构建的插件至少填 `1.28.0` |
| `entry.assembly` | 包内相对路径，必须是 `.dll` |
| `entry.type` | 实现 `IVclPlugin` 的公共类型，必须有公共无参构造函数 |
| `icon` | 可选，包内相对路径图片 |
| `window` | 窗口默认宽高与是否可调整；缺失时用 720×540、可调整 |
| `provides` / `requires` | 能力声明，见下文 |

包内不得包含宿主程序集（安装器会拒绝）：`VRCLearn.Plugin.Sdk`、`VRCLearn.Core`、`VRCLearn.Infrastructure`、`VRCLearnLauncher`、WPF/WPF-UI/CommunityToolkit/Microsoft.Extensions 相关程序集。插件的私有依赖可以随包携带。

## SDK 与入口

插件项目引用 SDK，目标框架为 `net10.0-windows` 并启用 WPF：

```xml
<ProjectReference Include="路径\VRCLearn.Plugin.Sdk\VRCLearn.Plugin.Sdk.csproj" />
```

```xml
<TargetFramework>net10.0-windows</TargetFramework>
<UseWPF>true</UseWPF>
```

宿主提供 WPF-UI 与 CommunityToolkit.Mvvm 的运行时副本；插件可以引用，但打包时不要放入 DLL。

以 `net10.0-windows` 为目标构建的插件，`minHostVersion` 至少写 `1.28.0`；旧版以 `net8.0-windows` 构建的插件仍可在 1.28.0 及以后的宿主中加载。

```csharp
public sealed class HelloPlugin : IVclPlugin
{
    private IPluginContext? _context;

    public void Initialize(IPluginContext context) => _context = context;

    public FrameworkElement CreateView() => new HelloView(_context!);
}
```

`Initialize` 每个进程只调用一次；`CreateView` 每次打开窗口调用。窗口关闭后视图会被释放，再次打开会重新创建。插件视图只应使用 `DynamicResource` 引用宿主主题资源，不要依赖宿主内部的 `StaticResource` 样式或类型，也不要缓存宿主窗口/视图对象。后台任务、事件订阅和静态状态由插件自行清理；必要时实现 `IDisposable` / `IAsyncDisposable`。

## IPluginContext 与宿主服务

```csharp
public interface IPluginContext
{
    string PluginId { get; }
    string PluginVersion { get; }
    string DataDirectory { get; }
    string HostVersion { get; }
    string Language { get; }
    IPluginLogger Logger { get; }
    IPluginLocalization Localization { get; }
    T? GetService<T>() where T : class;
    void RegisterService<T>(T service) where T : class;
    void CloseWindows();
    event EventHandler? LanguageChanged;
}
```

- `DataDirectory` 是宿主为插件创建的独立目录，卸载时会一并删除。
- `Localization` 读取插件包内 `localization/<语言>.json`；缺键时返回键名。
- `GetService<T>` 读取宿主服务或已初始化插件提供的服务；`RegisterService<T>` 供插件提供服务。
- `CloseWindows` 关闭本插件的所有窗口，用于跳转到宿主其他页面（例如代理设置）前收起插件。

### 代理与设置

```csharp
var proxy = context.GetService<IWebProxy>();
```

表示应用内代理设置。插件可以自行决定是否用于自己的 `HttpClient`；代理关闭时直接连接。

```csharp
public interface ISettingsHost
{
    void OpenNetworkSettings();
}
```

用于打开设置页的网络分区。跳转前需要收起插件窗口时，先调用 `IPluginContext.CloseWindows`。

### 提示弹窗

```csharp
public interface IPluginDialogHost
{
    PluginDialogChoice ShowChoice(string title, string message, string confirmText,
                                  string? extraText = null);
}

public enum PluginDialogChoice { Cancel, Extra, Confirm }
```

显示与宿主一致的确认弹窗：遮罩生效在插件窗口上，带标题栏关闭按钮，样式跟随宿主主题。`extraText` 为空时只显示确认按钮；关闭或取消返回 `Cancel`。

### 下载

```csharp
public interface IDownloadService
{
    Task DownloadAsync(DownloadRequest request, IProgress<DownloadProgress>? progress,
                       CancellationToken cancellationToken);
}

public sealed record DownloadRequest(Uri Url, string FilePath, string? Sha256 = null);

public sealed record DownloadProgress(long Received, long? Total);
```

- 只接受 `https://`，拒绝重定向到非 HTTPS 地址。
- 使用应用内代理，插件无需自行处理。
- 先写入 `<目标>.part`，完成后替换目标文件；取消或失败时删除临时文件。
- `Sha256` 非空时校验，不匹配则报错。
- 不内置下载源，下载源由调用方决定。

### 仓库

```csharp
public interface IRepositoryHost
{
    bool IsAdded(string url);
    Task<RepositoryAddResult> AddAsync(string url, CancellationToken cancellationToken = default);
}

public enum RepositoryAddStatus { Added, AlreadyAdded, InvalidUrl, Unreachable, InvalidContent }

public sealed record RepositoryAddResult(RepositoryAddStatus Status, string? Detail = null);
```

`IsAdded` 用于把已存在的仓库显示为「已添加」；`AddAsync` 会下载并校验仓库索引，成功后才写入配置。仓库地址由插件自己定义。

### 系统检测

```csharp
public interface ISystemCheckHost
{
    Task<IReadOnlyList<SystemCheckResult>> RunAsync(CancellationToken cancellationToken = default);
}
```

运行宿主内置的系统检测。

### 工程选择

```csharp
public interface IProjectPickerHost
{
    ProjectPickResult PickProject(ProjectPickerOptions? options = null);
    ProjectPickResult ValidateProject(string projectPath);
}

public sealed record ProjectPickerOptions(
    string? Title = null, string? Hint = null, string? Summary = null,
    string? Note = null, string? ConfirmText = null, string? EmptyText = null);

public enum ProjectPickStatus { Selected, Cancelled, NoProjects, NotAProject, Running }

public sealed record ProjectPickResult(ProjectPickStatus Status, string? ProjectPath = null,
    string? ProjectName = null);
```

`PickProject` 弹出宿主已有的项目选择框：运行时 Unity 未关闭的项目不可选。文案由调用方传入（插件自己本地化），留空用宿主默认文案。`ValidateProject` 不弹窗，只校验路径与运行状态。

## 插件之间的调用

跨插件的服务接口必须写在 `VRCLearn.Plugin.Sdk` 里——每个插件在独立的加载上下文中，插件自己定义的接口类型在另一边对不上，`GetService<T>` 会返回 `null`。

提供方在 `Initialize` 里注册服务（不要放在 `CreateView`）：

```csharp
public void Initialize(IPluginContext context) =>
    context.RegisterService<IGitUrlPackageInstaller>(new GitUrlInstaller(...));
```

消费方声明能力依赖，宿主会先加载并初始化提供方，再初始化消费方：

```json
"requires": [{ "capability": "giturl.package-install", "minVersion": 1 }]
```

```csharp
var installer = context.GetService<IGitUrlPackageInstaller>()
    ?? throw new InvalidOperationException("provider is missing");
```

内置契约 `IGitUrlPackageInstaller` 由「GitURL 包前置」插件提供，`InstallAsync` 自己完成下载、解压、选工程与确认，返回 `GitUrlInstallResult`，不会打开提供方自己的窗口。

用到新契约的插件要在 `plugin.json` 里把 `minHostVersion` 提到引入该契约的宿主版本，否则老宿主会因为没有这个接口而加载失败。

## 本地化

插件包内可选：

```
localization/
  zh-Hans.json
  en.json
```

```json
{
  "Hello.Title": "你好",
  "Hello.Run": "运行"
}
```

宿主按当前语言加载；找不到当前语言时依次回退到中性语言（如 `zh`）和 `en`。清单里的 `name` / `description` 由宿主负责本地化，插件视图里的文案由插件自己的 JSON 提供。

## 能力与依赖

能力用于表达“某个插件提供、另一个插件需要”的功能，不绑定具体插件 ID（`thumbnail` 只是示例）：

```json
"provides": [{ "capability": "thumbnail", "version": 1 }],
"requires": [{ "capability": "thumbnail", "minVersion": 1 }]
```

规则：

- 能力名以小写字母开头，允许小写字母、数字、`.`、`-`；版本为整数。
- 只有已启用且能正常加载的插件才参与解析。
- 多个提供者时，选择契约版本最高者；版本相同按插件 ID 排序。
- 提供者初始化失败时，依赖它的插件显示“加载失败”，不会静默换提供者。
- 缺少能力时允许安装，但不能启用/打开；安装提供者并启用后才可用。
- 被其他已启用插件依赖的提供者不能直接停用。

宿主服务不需要通过能力声明。例如下载直接获取 `IDownloadService`，不要声明或提供 `download` 能力。

示例插件维护在独立仓库，不随本仓库或安装包分发。

## 构建与打包

在仓库根目录：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build/pack-plugin.ps1 `
  -Project <插件项目.csproj> `
  -Configuration Release
```

脚本会：

1. `dotnet publish` 插件；
2. 复制 `plugin.json`、`localization/`、入口程序集与私有依赖；
3. 排除宿主程序集与 PDB；
4. 生成 `artifacts/plugins/<名称>-<version>.vclplugin`。名称优先取 `plugin.json` 的 `name.zh-Hans`，缺失时回退 `en`。

插件不随程序安装包分发。用户通过「从文件安装」或拖入 `.vclplugin` 手动安装。

## 安装与卸载

- 插件安装到 `<数据目录>\plugins\<id>\`，一个 id 只保留一份，安装即替换；不做版本并存、自动选版本或自动更新。
- 所有新安装的插件默认停用，必须在插件页手动开启；开启时宿主会提示插件拥有与程序相同的权限。
- 插件正在运行（DLL 已加载）时安装新版本，会显示“重启后生效”，下次启动先替换再加载。
- 插件启用时不能卸载；点击卸载会提示先停用。
- 卸载会同时删除插件文件与 `DataDirectory`。
- 卸载时先把插件目录移出扫描范围，再尝试删除；文件被占用时留到下次启动清理。

## 示例

主仓库内的 `plugin-template/` 是一个可复制改名的完整示例，包含清单、本地化、入口类型与 WPF 视图。修改 `plugin.json` 的 `id`、`entry` 与视图后，用 `pack-plugin.ps1` 打包即可。
