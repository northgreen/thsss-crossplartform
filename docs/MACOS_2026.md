# macOS（2026）

## 运行

默认渲染使用原生 Metal，音频使用 CoreAudio。

```sh
sh tools/run-game.sh
```

显式选择后端：

```sh
THSSS_RENDERER=metal \
THSSS_AUDIO_BACKEND=coreaudio \
  sh tools/run-game.sh
```

Intel 使用 `osx-x64`，Apple Silicon 使用
`osx-arm64`。Metal 设备不可用时明确报错。
原生调用依赖系统 Metal、QuartzCore、Objective-C
与 AudioToolbox，无需 .NET macOS workload。

## 打包

构建机需要 .NET 8 或 10 SDK、Perl、 完整 Git LFS 资源及两个着色器工具。
在仓库根执行：

```sh
git lfs install
git lfs pull
brew install glslang spirv-cross
sh tools/package-macos.command
```

此入口生成 Apple Silicon 自包含目录及压缩包。 默认目录为 `dist/thsss-YYYYMMDD-HHMMSS/`。
产物包含游戏、Config、运行时、只读资源、 当前 `Setting.INI` 和 `MetalShaders/`。
不复制开发目录的 Score、Replay 或 ScreenShot。

SDK 8 默认发布 net8.0，SDK 10 默认发布 net10.0。 可显式指定目标框架和独立输出目录：

```sh
sh tools/package-macos.command \
  --framework net8.0 --out dist/thsss-net8
sh tools/package-macos.command \
  --framework net10.0 --out dist/thsss-net10
```

SDK 10 可发布 net8.0；SDK 8 不评估 net10.0。 开发与回归通过 `THSSS_FRAMEWORK` 选择框架，
本地运行需要对应运行时。

通用分发入口支持显式 RID：

```sh
perl tools/make-dist.pl --rid osx-x64 \
  --self-contained --framework net8.0
```

打包缺少工具或 LFS 内容时失败，不启动游戏。 `--clean` 只允许清理 `dist/` 的真实子目录。
含玩家存档、Replay 或截图的输出目录不可复用。

接收端无需安装 SDK、运行时或转换工具。 解压后执行 `run-game.command` 或
`run-config.command`，也可运行同名 `.sh` 入口。
配置界面位于独立 `ConfigApp/`；启动器传递数据根。
依赖隔离见[字体与图像][fonts]。

[fonts]: FONTS_AND_IMAGES_2026.md

## 着色器缓存

共享 GLSL 经 glslangValidator 转 SPIR-V， 再经 SPIRV-Cross 转 MSL，由 Metal 编译。
缓存文件名由源码及阶段哈希组成。

运行时优先读取 `MetalShaders/*.msl`。 开发环境缺少缓存才调用转换工具；二者皆无时报错。
窗口呈现的小型 MSL 内置于程序。

导出工程位于 `tools/MetalShaderExporter/`， 直接读取生产 Sprite、3D 和 FX 源。
导出器使用构建机 RID 与 SDK 对应框架运行；
游戏和 Config 使用发布 RID 与目标框架。

单独导出：

```sh
sh tools/export-metal-shaders.sh 发布目录
```

## Metal 资源与帧生命周期

通过 GLFW 的 Cocoa NSWindow 附加 CAMetalLayer， 不另建窗口或输入循环。
游戏逻辑保持 640×480，drawable 跟随 framebuffer。
离屏行序、呈现坐标映射与黑边使用统一约定。

后端拥有纹理、共享缓冲、采样器、管线和命令队列。 外部深度附件为借用资源。 原生 selector 使用专用结构体与浮点 P/Invoke；
提交错误保留 NSError，每帧释放 autorelease 对象。

帧内按原绘制顺序录制命令。 连续相同目标和深度附件复用 render encoder。
目标、深度或清屏变化可开启新 pass； 呈现或 EndFrame 集中提交并等待。
无 BeginFrame 的独立调用保持立即完成语义。

每次 Draw 使用独立顶点快照，存入共享上传页。 提交完成后才能复用对应区域。
uniform 使用 setBytes 的复制语义。 CPU 读回、更新已用纹理或调整尺寸前先同步。
未使用的新纹理上传不强制拆分命令。

Sprite 变换在写顶点时生效，提交时使用恒等变换。
状态、目标、3D、Clear 或 FX 参数变化前 Flush。
字形纹理更新和释放也须先 Flush。

## 格式、采样与顶点输入

逻辑 RGB8 以 RGBA8 存储，Alpha 固定为 1。 RGB 上传补 Alpha，清屏固定 Alpha，管线只写 RGB。
普通 RGBA 源保留自身 Alpha，ADD 保留原混合公式。 管线缓存同时区分逻辑格式与写掩码。

Sprite Begin 默认建立 Clamp 与线性过滤， End 按保存标志恢复状态。
不改变 640×480 逻辑表面，不用缩放替代采样设置。

共享 3D shader 缺失的顶点属性由常量流补足： 槽 29 为 `(0,0,0,1)`，槽 30 为实际顶点，
uniform 使用槽 0–28。 只有存在缺失属性引用时才配置常量流布局并绑定。

渲染回调边界保存原异常并结束循环， 待框架复位后重新传播，保留原堆栈与非零退出码。

## 音频与文字

CoreAudio 共享持续运行的 44100Hz AudioQueue。 三个 256 帧缓冲承载软件混音。
Play/Pause/Clear 不启停原生设备队列。 游标按设备 SampleTime 与源位置映射推进。
具体循环、延迟与错误语义见[音频后端][audio]。

[audio]: AUDIO_BACKENDS.md

64 位 ABI 中，AudioStreamBasicDescription 为 40 字节，AudioTimeStamp 为 64 字节，
AudioQueueBuffer 数据指针偏移为 8 字节。

对白 CSV 按原资源的 CP936 编码读取。 字体由系统字体管理器选择，游戏使用 Skia/HarfBuzz。
缺字回退、测量和像素格式见[字体与图像][fonts]。 不下载或全局安装字体。

## 诊断与验证

```sh
THSSS_DIAG_INPUT=1 THSSS_DIAG_FPS=1 \
  sh tools/run-game.sh
sh tools/run-mac-regressions.sh
```

FPS 统计中的 waitMs 是 CPU 等待墙钟耗时。 音频耗时包含锁等待，不是纯设备耗时。

Linux 软件 API 测试需要 C 编译器、DejaGnu、 glslangValidator 与 spirv-cross，覆盖以下契约：

1. AudioQueue ABI、混音、游标、暂停和释放
2. Metal selector、上传、资源、顺序与呈现调用
3. 真实 GLSL 转 MSL，以及无转换工具时加载缓存

Metal 默认附件为单采样。 任意 BorderColor、MipLodBias 和非游戏使用的
扩展能力不属于完整兼容承诺。 OpenGL 可选路径需独立进行系统运行验证。
