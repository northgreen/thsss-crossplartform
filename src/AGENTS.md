# 当前跨平台源码

项目：`THSSS.csproj`，唯一正式开发入口。SDK读取仓库根 `global.json`。

开发运行通过根目录 `tools/run-game.sh`，它把数据根指到 `../game/`；直接运行已发布程序则使用发布目录自身资产。不要在这里额外复制玩家存档或创建第二套资源树。

发布内容引用 `../game/{FX,Model,Image,BGM,Sound,Story,CS,BPic}` 与初始Setting.INI；不发布Score.dat、Replay或ScreenShot。旧实现对照在 `../reference/decompiled/`。

兼容层直接使用 BCL 的 `System.ComponentModel` 类型，不重复声明 Component/IContainer 等类型。项目使用 nullable annotations（不打开旧代码的流分析）；设计器占位事件保存订阅但不新增触发行为，真实输入仍由 Dispatch 路径发送。未被 ResourceManager 访问的旧 `Shooting/Properties/Resources.resx` 不参与编译；主窗体资源清单独立保留。`BuildWarningCompatibility` 已纳入行为回归，验证事件订阅/退订、实际分派和类型/资源边界。

## 配置程序 `Config/`

`src/Config/THSSS.Config.csproj` 是原版 `Config.exe` 的跨平台替代（Avalonia 11，net8.0 / net10.0，输出名 `Config`）。项目共用根 `TargetFrameworks.props`，SDK 8 仅启用 net8.0，SDK 10 启用双目标。仅改动 `game/Setting.INI` 中原对话框拥有的键：`[Key]` 七项、`[DirectX]` 的 VerticalSync/FullWindow、`[Mode]` 的 PresentInterval、`[DirectInput]` 的 UseDirectInput、`[JoyStick]` 五项、`[Game]` 的 FontType。不要在这里加入音量、Clear、渲染器开关等原对话框不写的键。

`IniFile` 按 UTF-8 尝试、失败回退 GBK，保存时沿用读入编码并原子替换；`KeyNameMapper` 只输出游戏 `LegacyKeyMap` 能解析的键名。`ConfigWorkspace` 通过 `THSSS_DATA_ROOT` 或可执行文件同目录定位数据。回归：`tests/testcase/ConfigCompatibility`（已纳入 `tools/run-behavior-regressions.sh`）。

## 字体与图像

游戏仅使用 SkiaSharp 4.154.0 / HarfBuzzSharp 14.2.1.400；`RasterCodec` 负责顶向下的 straight-alpha RGBA/BGRA 边界，Skia 绘制表面使用 premultiplied alpha 后必须显式转换。`SkiaTextFonts` 使用平台字体管理器（Linux 为 fontconfig），`SkiaTextLayout` 同时供测量与绘制，不能再引入固定目录扫描或手写别名替代系统配置。回归 `SkiaCompatibility`、`TextScreenshotCompatibility` 和 shader 套件。

发布时根 `Config` 是 `ConfigLauncher/` 的 BCL 启动器；Avalonia GUI 及原 Skia 2.88.9/HarfBuzz 8.3.1.1 依赖必须放 `ConfigApp/`，不能覆盖游戏根的同名库。开发入口 `tools/run-config.sh` 仍直接运行 `Config/` 项目。源码 glob 必须排除这两个独立项目。第三方原始许可位于 `third_party/SkiaSharp/`，分发不要求商业许可密钥。
