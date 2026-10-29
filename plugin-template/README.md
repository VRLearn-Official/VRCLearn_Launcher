# 插件模板

`VRCLearn.Plugin.Template` 是一个最小可编译插件。在主仓库内复制整个目录并改名即可开始：

1. 修改项目文件名、`AssemblyName` 与 `RootNamespace`。
2. 修改 `plugin.json` 的 `id`、`name`、`description`、`entry.assembly`、`entry.type`。
3. 把 `TemplatePlugin` / `TemplateView` 改成自己的类型和界面。
4. 在仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build/pack-plugin.ps1 `
  -Project plugin-template/VRCLearn.Plugin.Template/VRCLearn.Plugin.Template.csproj
```

得到 `artifacts/plugins/示例插件-1.0.0.vclplugin`（文件名取自 `plugin.json` 的 `name`），在启动器的【插件】页用「从文件安装」选择它。

完整规范见 [`docs/plugins.md`](../docs/plugins.md)。
