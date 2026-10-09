# 兼容基线

| 工具 | 最低版本 | 说明 |
| --- | --- | --- |
| Visual Studio | 2022 17.8 | `net8.0` 目标的官方支持下限；解决方案为经典 `.sln`（Format Version 12.00），17.13 之前无需预览特性 |
| .NET SDK | 8.0.100 | `global.json` 声明的基线，`rollForward: latestMajor` 允许使用任意更高版本 SDK |
| MSBuild | 17.8 | 随 VS 2022 17.8 附带 |
| NuGet | 6.2+ | 中央包管理（`Directory.Packages.props`）要求 VS 2022 17.2 / NuGet 6.2 起 |
| .NET Framework | 4.8（Developer Pack） | `net48` 目标所需；仓库已确认本机存在 v4.8 targeting pack |

## 各工程目标框架

| 工程 | 目标框架 | 与工具链的关系 |
| --- | --- | --- |
| `GameTools.Core` | `netstandard2.0` | **不参与多目标构建**。契约与值对象层刻意只面向单一框架，避免被任一 UI 框架或 .NET 版本绑定 |
| `GameTools.Win32` | `net8.0-windows`;`net48` | 需要 `net8.0-windows` 与 v4.8 targeting pack |
| `GameTools.Infrastructure` | `net8.0-windows`;`net48` | 同上；`net8.0-windows` 侧额外需要 .NET 桌面目标包内置的 UI Automation 程序集 |
| `GameTools.App` | `net8.0-windows`;`net48` | 同上 |
| `GameTools.Tests` | `net8.0-windows`;`net48` | 同上 |

`net48` 面向 Windows 7 SP1 / 8 / 8.1，`net8.0-windows` 面向 Windows 10 及以上。配置环境时若参照本表误以为 `Core` 也是双目标，会得到错误的还原与裁剪预期。

## 约束来源

- `.slnx` 解决方案格式仅 VS 2022 17.13+ / SDK 9.0.200+ 原生支持，17.12 需手动开启预览特性。为兼容老版本，统一使用经典 `.sln`。
- `net8.0-windows` 目标要求 VS 2022 17.8 及以上（8.0.100 可在 17.7 加载但不支持 net8.0）。
- `Directory.Packages.props` 中央包管理由 VS 2022 17.2 / NuGet 6.2 引入；低于此版本会忽略集中版本配置，导致还原失败。

## 后续引入新特性时的检查清单

1. 是否仅在 VS 17.13+ 或 SDK 9.0.200+ 可用？若是，不得引入。
2. 是否属于仅 .NET 9/10 SDK 提供的 MSBuild 属性或分析器规则？若是，不得写入 `Directory.Build.props`。
3. 是否使用了 `global.json` 中 `rollForward: latestMajor` 之外的新回滚策略？
4. 引入后必须在 SDK 8.0.100 下完成一次完整双目标构建与测试，不得只在最新 SDK 下验证。

## 已验证矩阵

| SDK | 解决方案 | 构建 | net8.0-windows 测试 | net48 测试 |
| --- | --- | --- | --- | --- |
| 8.0.420 | `GameTools.sln` | 0 警告 0 错误 | 13 通过 | 13 通过 |
| 10.0.401 | `GameTools.sln` | 0 警告 0 错误 | 13 通过 | 13 通过 |

未验证：VS 2022 17.8 图形界面内的实际加载与调试体验（本机无该版本 VS），仅以 SDK 8.0.420 命令行构建与测试作为等价验证。