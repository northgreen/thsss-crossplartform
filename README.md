# 东方夏夜祭 · 跨平台移植

移植原版thsss（东方夏夜祭）到更现代的dotnet并且适配权平台

## 运行

```sh
sh tools/run-game.sh
```

支持 .NET 8 和 .NET 10；macOS 默认使用系统 Metal / AudioToolbox，Windows 使用 OpenGL / XAudio2，Linux 使用 OpenGL / OpenAL。启动脚本自动使用 `game/` 的资源、配置与存档；不必再手工设置工作目录。

SDK 统一由根目录 `global.json` 选择：最低 .NET 8，优先使用已安装的最新正式 SDK。游戏、Config、着色器导出器及回归项目共用 `TargetFrameworks.props`；SDK 8 只构建 `net8.0`，SDK 10 同时提供 `net8.0` / `net10.0`。启动及回归脚本在 SDK 10 下默认选择 `net10.0`，SDK 8 下默认选择 `net8.0`，可用 `THSSS_FRAMEWORK=net8.0 sh tools/run-game.sh` 显式选择。本地运行需要对应版本的运行时，SDK 10 本身不等于安装了运行时 8；自包含发布包会携带所选运行时。

## 配置

```sh
sh tools/run-config.sh
```

原版 `Config.exe` 的跨平台替代（Avalonia，Linux/macOS/Windows 同一份程序）。它读写同一个 `game/Setting.INI`：显示（全屏启动/垂直同步/绘制周期）、键盘与手柄按键、字体。点“应用”保存，点“应用并启动”保存后启动游戏。中文值按 GBK 保留，未改动的音量等键不会被重写。

## 找东西

| 目录 | 内容 |
|---|---|
| [`src/`](src/) | **当前唯一开发项目**：游戏 `src/THSSS.csproj`、配置程序 `src/Config/THSSS.Config.csproj` |
| [`game/`](game/) | **实际运行数据**：图片、音乐、模型、脚本，以及 `Setting.INI`、`Score.dat`、`Replay/`、`ScreenShot/` |
| [`reference/`](reference/) | **仅供参考**：`original/` 原版EXE/SlimDX/提取资源，`decompiled/` 反编译源码，`THSSS-Refactored/` 早期Windows尝试 |
| [`docs/`](docs/) | 审计与修复记录、原作说明；先看 [`docs/README.md`](docs/README.md) |
| [`tests/`](tests/) · [`tools/`](tools/) | 回归测试、启动与验收脚本 |

## 构建与发布

M 系列 Mac 在首次准备好 .NET 8 或 10 SDK、Git LFS 资源和着色器工具后，从仓库根运行一个命令：

```sh
# 首次准备；已拉取 LFS 资源时不必重复
git lfs install
git lfs pull
brew install glslang spirv-cross

sh tools/package-macos.command

# 明确选择发布版本；两个包使用各自的目录
sh tools/package-macos.command --framework net8.0 --out dist/thsss-net8
sh tools/package-macos.command --framework net10.0 --out dist/thsss-net10
```

默认输出为 `dist/thsss-日期-时间/` 和同名 `.tar.gz`，包含 ARM64 游戏、Config、所选 .NET 运行时、资源和 Metal 着色器。省略 `--framework` 时，SDK 10 默认打包 .NET 10，SDK 8 默认打包 .NET 8；发布 `net10.0` 需要 SDK 10。解压后双击 `run-game.command` 或 `run-config.command`。测试机无需另装 .NET、Homebrew 或着色器工具。此处是可携带的目录包，不是经过 Apple 公证的安装器；详细入口与验证边界见 [macOS 文档](docs/MACOS_2026.md)。

未拉取的 LFS 指针会让打包在构建前终止，并列出缺失资源示例。打包脚本不会自动下载资源。

其他平台的普通构建/发布入口：

```sh
dotnet build src/THSSS.csproj -f net8.0
dotnet build src/Config/THSSS.Config.csproj -f net8.0
dotnet publish src/THSSS.csproj -f net8.0 -c Release -o artifacts/publish
dotnet publish src/Config/THSSS.Config.csproj -f net8.0 -c Release -o artifacts/publish
```

SDK 10 下将 `-f net8.0` 换为 `-f net10.0` 即可构建另一版本；直接 `dotnet build` 会构建当前 SDK 支持的所有目标。发布版会附带只读资源和 `game/Setting.INI` 中的当前配置，运行后在自己的目录创建存档/录像；**不会把 `game/Score.dat` 或你的录像打进发布包**。已有玩家数据只需保留在 `game/`，不要拿测试数据覆盖。

## 验证

```sh
sh tools/run-shader-regressions.sh
sh tools/run-behavior-regressions.sh
```

无图形桌面时可用 `ALSOFT_DRIVERS=null xvfb-run -a sh ...`。硬件手柄、扬声器听感、多刷新率和原版逐帧对照的边界见当前修复报告。

本次目录整理保留所有文件内容；没有删除或重置原版资源、存档、录像、配置和弹幕脚本。
