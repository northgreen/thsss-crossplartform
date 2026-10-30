# 当前跨平台源码

项目：`THSSS.csproj`，唯一正式开发入口。SDK读取仓库根 `global.json`。

开发运行通过根目录 `tools/run-game.sh`，它把数据根指到 `../game/`；直接运行已发布程序则使用发布目录自身资产。不要在这里额外复制玩家存档或创建第二套资源树。

发布内容引用 `../game/{FX,Model,Image,BGM,Sound,Story,CS,BPic}` 与初始Setting.INI；不发布Score.dat、Replay或ScreenShot。旧实现对照在 `../reference/decompiled/`。

## 配置程序 `Config/`

`src/Config/THSSS.Config.csproj` 是原版 `Config.exe` 的跨平台替代（Avalonia 11，net8.0 / net10.0，输出名 `Config`）。项目共用根 `TargetFrameworks.props`，SDK 8 仅启用 net8.0，SDK 10 启用双目标。仅改动 `game/Setting.INI` 中原对话框拥有的键：`[Key]` 七项、`[DirectX]` 的 VerticalSync/FullWindow、`[Mode]` 的 PresentInterval、`[DirectInput]` 的 UseDirectInput、`[JoyStick]` 五项、`[Game]` 的 FontType。不要在这里加入音量、Clear、渲染器开关等原对话框不写的键。

`IniFile` 按 UTF-8 尝试、失败回退 GBK，保存时沿用读入编码并原子替换；`KeyNameMapper` 只输出游戏 `LegacyKeyMap` 能解析的键名。`ConfigWorkspace` 通过 `THSSS_DATA_ROOT` 或可执行文件同目录定位数据。回归：`tests/testcase/ConfigCompatibility`（已纳入 `tools/run-behavior-regressions.sh`）。
