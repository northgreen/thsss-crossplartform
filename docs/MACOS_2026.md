# macOS（2026-10-29）

## 运行方式

macOS 默认渲染后端为原生 Metal，音频为 AudioToolbox AudioQueue（`coreaudio`）。不经 MoltenVK，也不把 OpenAL 作为默认音频依赖。

```sh
sh tools/run-game.sh
# 显式选择：
THSSS_RENDERER=metal THSSS_AUDIO_BACKEND=coreaudio sh tools/run-game.sh
```

Intel 对应 `osx-x64`，Apple Silicon 对应 `osx-arm64`。Metal 需要系统提供 Metal device；设备不可用会明确报错，不静默切换渲染器。原生调用使用系统 Metal、QuartzCore、Objective-C runtime 与 AudioToolbox framework，不要求 .NET macOS workload。

## 实现契约

- `IRenderBackend` 下的 Metal 后端拥有纹理、共享缓冲、深度附件、采样器、管线及命令队列。Cocoa NSWindow 由现有 GLFW 窗口取得，附加 CAMetalLayer；不另建不兼容的窗口/输入循环。
- 图像逻辑保持640×480；drawable按实际Framebuffer尺寸更新。离屏纹理使用与既有后端一致的行约定，呈现shader只进行一次坐标映射，保留完整画面和黑边。
- 各次绘制使用真实Metal selector与专用结构体/浮点P/Invoke签名；提交错误转换为包含NSError的异常。每帧释放autorelease对象，显式资源销毁时解除追踪，外部深度附件不转移所有权。
- Metal使用共享GLSL源经glslangValidator→SPIR-V→SPIRV-Cross MSL转换。游戏实际Sprite、3D、Blur/Filter/Warp均覆盖；Metal系统编译生成的MSL，不复制另一套效果公式。
- CoreAudio 接受公共层每个 voice 处理输出矩阵后的交错 float32 立体声块，以一条持续运行的 44100Hz AudioQueue 输出软件混音。不同采样率按整数相位线性插值；循环点仍由公共播放层处理。回填回调不代表已播放，游标和退休采用设备 SampleTime 与每段混音的源位置映射。Pause/Clear 只更新该 voice，Dispose 在释放回调锁后同步等待系统释放输出。
- `AudioStreamBasicDescription`为40字节，AudioTimeStamp为64字节，AudioQueueBuffer数据指针偏移为8字节（64位ABI）。

## 发布与着色器

M 系列 Mac 的目标 RID 为 `osx-arm64`。构建机需要 .NET 8 或 10 SDK、Perl（macOS 系统提供）、完整 Git LFS 资源，以及 `glslangValidator`、`spirv-cross`。一次准备后用单一入口打包：

```sh
git lfs install
git lfs pull
brew install glslang spirv-cross
sh tools/package-macos.command
# 可选：明确指定版本，使用独立输出目录
sh tools/package-macos.command --framework net8.0 --out dist/thsss-net8
sh tools/package-macos.command --framework net10.0 --out dist/thsss-net10
```

默认产物为 `dist/thsss-YYYYMMDD-HHMMSS/` 和同名 `.tar.gz`，包含游戏、Config、所选 .NET ARM64 运行时、只读资源、当前 `Setting.INI` 和 `MetalShaders/`。SDK 8 默认发布 `net8.0`，SDK 10 默认发布 `net10.0`；SDK 10 也可用 `--framework net8.0` 发布 .NET 8 包，只有 SDK 8 时不会评估不支持的 .NET 10 目标。解压后双击 `run-game.command` / `run-config.command`，也可用 `sh run-game.sh` / `sh run-config.sh`。接收测试包的 Mac 无需安装 SDK、运行时或转换工具。打包不启动游戏；缺少工具或 LFS 内容时会明确失败，不把指针文件装进包里。

打包入口调用 `tools/make-dist.pl --rid osx-arm64 --self-contained`，可追加 `--out dist/自定义名称`；默认每次使用新的时间戳目录，避免混入旧的运行产物。`--clean` 仅允许删除仓库 `dist/` 下已解析真实路径的子目录。已有 `Score.dat`、Replay 或 ScreenShot 内容会让复用发布目录失败。

`MetalShaders/*.msl` 文件名由源代码及阶段哈希组成，避免误用过期缓存。运行时先查发布目录缓存，开发环境缺少缓存才调用转换工具；二者皆不可用时给出明确错误。缓存包含实际游戏着色器，Metal窗口呈现的小型MSL随程序内置。

生产导出工具为 `tools/MetalShaderExporter/MetalShaderExporter.csproj`，直接读取游戏实际使用的 Sprite、3D 和 FX 着色器源，不解析 C# 文本或运行测试程序集。导出器按构建机 RID 和 SDK 对应的框架运行，游戏和 Config 按发布 RID 与 `--framework` 构建；SDK 10 交叉发布 .NET 8 包时，导出器仍在本机 .NET 10 上运行，不要求额外安装运行时 8。单独导出仍可运行 `sh tools/export-metal-shaders.sh 发布目录`。包内 `README.txt` 记录实际目标框架，复用发布目录时同时核对游戏与 Config 的 runtimeconfig，避免把两个版本混在一起。

开发运行及回归入口共用 `THSSS_FRAMEWORK`，例如 `THSSS_FRAMEWORK=net8.0 sh tools/run-mac-regressions.sh`；本地执行哪个版本就需要该版本运行时。双版本配置已在 Windows 用 SDK 10 构建 net8.0 游戏、Config 和导出器，均为 0 个错误；主机跟踪确认实际加载 .NET 8.0.13。真实游戏在隔离数据目录完成菜单、进关、连续射击、Bomb、移动与 Alt+F4，退出码 0；录像记录 930 个关卡帧，其中 544 帧含射击，关卡期间 FPS 日志约 59–61。原配置和存档哈希未变。该结果不等于 SDK 8 本身或 Mac .NET 8 包已经验证；Mac 双版本打包及完整回归尚未执行。

配置程序为独立 Avalonia 工程，按相同 RID 发布。“应用并启动”显式传递正在编辑的数据根，避免配置与游戏使用不同的存档目录。包不会复制开发目录里的 Score/Replay/ScreenShot。

## 精灵提交顺序

`Sprite.Transform` 在每次 `Draw` 写入顶点时生效，保留四维齐次坐标；提交时给普通和 FX 着色器的变换均为恒等矩阵，避免重复变换。同一纹理的不同旋转/缩放可以在一批中提交，纹理比较使用 Texture 对象，不依赖每次可能重新分配的原生句柄包装。

混合/深度状态、目标/深度附件、Clear、3D 绘制、FX 开始/结束及参数变化之前提交待绘批次，保持原先的绘制顺序。文字显式 `Flush` 后才允许更新或释放字形纹理，不再依赖 Transform 自赋值的副作用。Sprite uniform 缓冲复用，普通提交不再创建三个临时数组。

## 中间纹理与透明度

原版 `ScreenTexture` 使用 `X8R8G8B8` 屏幕中间目标，没有可写 Alpha。Metal 现提供 RGB8 逻辑语义：物理仍用 RGBA8 存储，上传 RGB 时补 Alpha=1，清颜色时固定 Alpha=1，绘制管线只允许写 RGB（writeMask=14）。源纹理和顶点的 Alpha 仍正常参与 RGB 混合，普通 PNG/RGBA 纹理保留自己的 Alpha。

此前 Metal 没有声明 RGB8 支持，兼容层把这些目标替换成普通 RGBA。半透明图案会改变中间目标的 Alpha，最终 Filter 再按它混合整张场景，造成重复衰减。用户反馈的道具得分透明度异常及敌人发弹特效发黑均经过这条合成路径。本轮修复格式语义，不将 PNG 或 shader 输出整体改为预乘 Alpha，也不修改 ADD 的 SrcAlpha/One 公式。

管线缓存同时区分逻辑 RGB 与 RGBA；两者的原生像素格式同为 RGBA8，但 Alpha 写掩码不同。已有软件 API 驱动增加 RGB/RGBA 清屏 Alpha、普通透明与 ADD 因子、写掩码及缓存切换断言；本轮仅静态检查，未运行驱动或进行 Apple GPU 画面验证。

## 精灵过滤与对话文本

Mac 对照图中的太极纹理出现大块纹素阶梯。静态路径确认 3D 绘制会按 D3D9 默认设置点采样，而精灵绘制未建立自己的采样状态，Metal/Vulkan 会沿用上一次 sampler。Sprite 现按 D3DXSprite.Begin 规则建立 Clamp + 线性 Min/Mag/Mip，并在每次 Flush、FX 绑定之前应用当前设备采样状态。End 默认恢复 Begin 前的状态；DoNotModifyRenderState 保留调用方设置，DoNotSaveState 不恢复。快照字典随 Sprite 复用，不在每次 Begin 分配；没有增大 640×480 逻辑表面或添加 MSAA。

现有 Vulkan 实际 Sprite 检查增加了放大红蓝双纹素的线性插值，以及 End 恢复点采样、DoNotModifyRenderState 保留点采样的回归路径；本轮没有运行，Mac 视觉差异仍待复测。

对话 A 路线的 LoadConversation2 原用 Encoding.Default；现代 .NET 将其解释为 UTF-8，而实际 Story/1-1A.csv 是 CP936，按 UTF-8 严格解码失败。现与其他原版对白路径一致，显式使用 CP936，未重编码或修改资源文件。字体选择包含已安装的 Kaiti SC/STKaiti、Songti SC/STSong、PingFang SC 等 Mac 中文候选；不下载或全局安装字体。

Mac 实机在菜单版本号排版时曾报 MissingFontTableException（loca）。Drawing 2.1.5 的依赖下界为 Fonts 2.0.8；该版本通过 FileFontMetrics 延迟读取完整字体，TextLayout 会对全部回退字体执行 UpdatePositions，即使正文只有 ASCII。字体名称存在不能证明该字面可被解析，不能把全部系统候选直接塞入 FallbackFontFamilies。

主字体和回退字体现均先读取 FontMetrics.UnitsPerEm，触发完整字面解析；仅在候选选择边界隔离 InvalidFontFileException、FontException 和解析器的 NotSupportedException，输出 `[FONT]` 的字体名、请求样式、文件路径和完整异常，再尝试其他已安装候选。Fonts 2.x 中 MissingFontTableException 继承 InvalidFontFileException，而后者直接继承 Exception，不属于 FontException 分支；只捕获 FontException 接不住缺失 loca 的错误。Windows .NET 8 实跑还发现已安装 PingFang.ttc 在 CffParser.ReadFDArray 抛出 NotSupportedException，纳入此边界后同一路径完成进关及射击。按字体族与请求样式缓存验证结果，避免逐帧重新解析或重复报告。回退只保留一个已经验证并含中文字形的字体族，排除主字体自身，避免重复定位；测量、换行和实际绘制共用同一选择规则。找不到任何可解析字体仍明确失败，排版和绘制异常不在此处吞掉。

已有文字检查增加了临时缺失 loca 字体：从已安装 TrueType 字体的副本移除表标记，只写临时目录，验证元数据可读但完整解析失败的候选被排除、完整错误被保留且失败结果可复用。本轮仅静态检查，未构建、未运行该检查或进行 Mac 菜单/对话复测；GBK 和采样修复保留。

## 3D 输入与异常退出

部分原版背景使用只有 Position/UV 的顶点流，而共享 3D shader 仍声明 Normal/Color/Secondary 输入。Metal 管线创建要求描述其保留的输入，不能依赖 `uHasNormal` 等运行时开关消除描述符要求。后端用槽 29 的常量流 `(0,0,0,1)` 补足缺失属性，`stepFunction=constant`、`stepRate=0`，由 Metal 按 shader 类型截取分量，保持 GL 默认属性值语义；真实顶点流仍在槽 30，uniform 使用槽 0–28。仅当存在属性引用时才配置相应流布局：没有缺失属性的管线不配置槽 29，也不绑定常量数据。是否使用常量流与管线一起缓存，创建和绘制使用同一条件，避免无属性引用的非零 stride 布局触发原生断言。没有修改原版背景顶点数据或关卡逻辑。

Silk.NET 2.23 的渲染回调抛出异常时不会复位内部 render-loop 标记，随后的窗口 Dispose 会再次抛出 Reset 异常。入口在渲染回调边界记录 `ExceptionDispatchInfo` 并结束循环，让框架正常复位标记后再传播原异常，保留原始堆栈和非零退出码。

## 帧内录制与同步

M 系列 Mac 的实机记录 `thsss-perf-20261029-233645.log` 显示前段约 60 FPS、mainMs 约 16ms，进入关卡后降至 7.12 FPS、mainMs 139.54ms。另一进程的 `thsss-sample-20261029-233900.txt` 中，主线程 3663 个调用栈样本有 2951 个处于 `waitUntilCompleted`，约 80.6%。这是主线程采样中的等待占比，不是 CPU 利用率或 GPU 执行时长。音频 Pause/Stop 也有同步等待，但 Metal 逐次提交和等待是该采样的主要热点。

Metal 在 BeginFrame/EndFrame 之间按原顺序录制命令，同目标和深度附件的连续绘制复用 render encoder，在呈现或 EndFrame 集中提交并等待。目标、深度附件或清屏操作需要新的 pass，但不为每次 Sprite/3D Draw 单独提交。没有 BeginFrame 的独立 API 调用保留立即完成的语义。

每次录制的 Draw 使用独立顶点快照，写入可复用的共享上传页；页内区域只有在提交完成后才能复用，不能继续把反复改写的 Sprite VBO 直接交给延迟执行的 GPU。uniform 使用 Metal setBytes 的复制语义。CPU 读回、改写待提交命令已经使用的纹理或调整 framebuffer 尺寸时先同步；新建且尚未使用的纹理上传不会无条件拆分命令。命令缓冲使用默认保留资源引用的创建方式。

`THSSS_DIAG_FPS=1` 约每秒输出一次统计。Metal 的 `submits`、`draws`、`passes`、`waitMs`、`uploadKiB` 是本统计区间的每帧平均值：draws 不包含最终呈现三角形，passes 包含呈现；waitMs 是 CPU 在提交后同步等待的墙钟耗时，不能当作纯 GPU 时间。没有 CPU 读回或已用纹理改写时，普通呈现路径应集中为一次提交；特殊同步操作可增加提交次数。

## 音效重播与射击卡顿

`thsss-perf-20261030-000848.log` 中 Metal 始终约一次提交/帧，等待约 1–2ms；卡顿区间音频却反复出现 Pause 约 32ms、Clear 约 11ms、Start 约 10ms。frame=2162 的区间为 8.03 FPS、mainMs 124.16；按日志相邻统计区间汇总，音频操作累计约 117.81ms/帧（包括音频泵线程的日志，不能视为精确的主线程独占耗时）。灵梦等自机每三个逻辑帧请求一次射击音效，反复进入 Stop/Flush/Submit/Start；移动改变声像时旧顺序还会先重建马上要清除的 PCM。

音效播放器现先 Stop/Flush，再设置新声像并 Submit/Start；不改变原版射击间隔或音效请求。CoreAudio 后端以一条持续输出队列混合各 voice，Play/Pause/Clear 不再启停原生设备队列，也不为每次重播分配原生缓冲。三个 256 帧缓冲的 PCM 合计约 17.4ms；已交给系统的混音无法逐 voice 撤回，暂停和重播仍受这段缓冲及实际设备延迟影响。软件游标在暂停时冻结，只有设备时钟经过的源采样才计为已播放。

要记录实际 AudioQueue 操作耗时，在新发布目录运行：

```sh
THSSS_DIAG_INPUT=1 THSSS_DIAG_FPS=1 sh run-game.sh
```

`[AUDIO-TIMING]` 分别输出每个 voice 的 Pause/Clear/Start 耗时、source 编号和 transport=shared，默认关闭。`[AUDIO-OUTPUT]` 单独记录一次原生输出启动。耗时包含操作期间的锁等待，不把这些日志当作整个键盘输入路径的采样。

已有 M 系列 Mac 反馈确认帧内录制后非射击区间可到约 60 FPS，持续射击时仍可降至约 8–25 FPS，输入源为英文/ABC。本轮据音效重播日志改为共享持续输出，仅在 Windows 静态检查，未构建、未打包、未运行测试或游戏，也未修改玩家数据。CoreAudio 软件 API 驱动已随实现更新但未运行；共享输出的实体设备听感、暂停响应和射击帧率尚待复测。

## 自动验证

```sh
sh tools/run-mac-regressions.sh
```

Linux x86_64验收环境需要C编译器、DejaGnu、glslangValidator与spirv-cross。三条DejaGnu用例：

1. AudioQueue原生ABI与软件队列驱动：预取回调、游标、暂停恢复、退休与重置。
2. 实际Metal后端P/Invoke的软件Objective-C/Metal驱动：selector和结构体参数、上传行距、清屏/读回、命令顺序、错误传播、所有权以及Cocoa drawable呈现。
3. 实际游戏GLSL经过真实编译器生成MSL；另测试无转换工具PATH时仍可载入全部发布缓存。

软件驱动不会执行MSL光栅化。清屏/存储像素和调用顺序是真实跨原生边界的测试；呈现方向另由实际转换后的MSL及非对称坐标断言核对，不冒称Apple驱动截图或扬声器听感测试。`osx-x64`、`osx-arm64`交叉发布验证编译与资源；Linux既有GL/Vulkan回归独立运行。

## 边界

没有把缺少GPU加速当成测试不可开展的理由。上述软件API驱动可验证调用契约，但不能替代Apple系统实现；本环境尚未完成Apple运行时的完整游戏/音频输出测试，Apple Silicon的调用约定仅交叉编译和结构布局核对。macOS签名、公证、安装器与实体设备性能不在当前自动验收范围。

默认Metal附件为单采样；MSAA开关没有制造虚假多采样能力。采样器的任意自定义BorderColor、MipLodBias及非当前游戏使用的拓展功能不应被宣称完整支持。

OpenGL保留既有实现且未因macOS而降级。Sampler objects在OpenGL 3.3已进入核心，`RGB8`也是合法内部格式；不能以“它们需要4.2/在core非法”为依据删除已验证的路径。macOS可选GL路径的最终系统运行验证独立于Metal测试。
