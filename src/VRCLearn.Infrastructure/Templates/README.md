# 内置项目模板

这些是新建项目时用的 Unity 项目骨架，按「类型-Unity版本」分目录。

只随程序发布受支持的那一版（目前是 `2022.3.22f1`，见 `UnityReleaseCatalog.StableVersion`）。

## 来源

由 Unity 2022.3.22f1 创建的空工程生成，安装 VRChat SDK 3.10.5 后取以下内容：

- `ProjectSettings/`（含 `ProjectSettings/Packages` 下的包配置）
- `Packages/manifest.json`
- `Packages/vpm-manifest.json`

VRChat SDK 包本身、`Assets/` 里的示例场景与脚本、`Library` / `Temp` / `Logs` / `UserSettings` 都不进模板。新项目由启动器按 `vpm-manifest.json` 解析并安装依赖；清单声明 `3.x`，创建时解析当前最新的 3.x，而不是钉死在快照用的 3.10.5。

## 建项目时被覆盖的字段

`ProjectSettings/ProjectSettings.asset` 里有两处会在建项目时替换（`ProjectCreator`）：

| 字段 | 模板里的值 | 建成后 |
|---|---|---|
| `productGUID` | 模板工程的 GUID | 新生成的 GUID |
| `productName` | `Avatar` / `Worlds` | 项目名 |
