# 渲染后端（2026-10-28）

## 选择

| 系统 | 默认 | 可选 |
|---|---|---|
| Linux、Windows | OpenGL | Vulkan |
| macOS | 原生Metal | OpenGL作为兼容路径 |

`THSSS_RENDERER=gl|vk|metal`。未知值明确报错；设备或窗口表面不可用不静默替换API。macOS Metal实现、构建依赖与验证边界见[MACOS_2026.md](MACOS_2026.md)。Vulkan窗口仅接通Win32/X11/Wayland，没有宣称MoltenVK窗口支持。

## 契约

`IRenderBackend`包含资源、uniform块、绘制、采样/混合/深度状态、帧生命周期与呈现。原版D3D9兼容层保持游戏逻辑坐标640×480，窗口按Framebuffer像素尺寸渲染，详见[WINDOW_SCALING_2026.md](WINDOW_SCALING_2026.md)。

OpenGL由窗口层持有上下文并交换缓冲。Vulkan与Metal使用无GL上下文窗口，各自管理原生设备及呈现。兼容层仍有GL专用优化/诊断分支，不能声称所有文件完全无GL引用。新后端需实现资源所有权、状态映射与native窗口入口，而不是修改关卡或弹幕公式。

Vulkan使用1.3特性，帧内批量录制并快照顶点/uniform数据，fence完成后回收描述符和上传内存。资源销毁、读回或更新需要时允许flush；不能把“一帧一提交”视为所有场景不变的约束。静态文字仅缓存每个字体最近一份字形纹理，避免无限增长。

Sprite 在 Draw 时将当前变换写入齐次顶点，同纹理的旋转/缩放不再各自强制提交；原生句柄包装对象的引用不参与合批判断。Device/FX 在改变绘制状态和目标前提交待绘顶点，文字使用显式 Flush 保证缓存纹理生命周期。2026-10-29 的这组改动仅静态审查，尚未重新构建或取得设备性能数据，详见 macOS 文档。

Metal直接调用系统框架，不经MoltenVK；MSL由同一份GLSL转译。发布包预存MSL后不需要运行时转换工具；开发环境可用glslangValidator与spirv-cross。Vulkan当前仍需要glslangValidator运行时转换，不能把Metal缓存描述套用到Vulkan。

Mac 实机采样中主线程约 80.6% 的样本停在 Metal `waitUntilCompleted`，对应旧实现逐次 Draw/Clear 提交并等待的路径。Metal 现改为帧内录制、相同目标和深度的 encoder 复用、顶点上传快照，以及呈现/帧结束集中提交；CPU 读回、已用纹理更新和尺寸变化仍先同步。本轮仅静态修改，尚无优化后帧率或画面验证结果。

## 状态与资源

- Device状态更新与是否存在GL上下文无关，所有非GL后端也必须收到混合、深度写入与光照设置。
- `Usage.RenderTarget`走渲染纹理工厂，原生图像需要合法附件用途。
- Sprite 按 D3DXSprite.Begin 建立线性过滤和 Clamp，在 Flush 的 FX 绑定前应用设备采样状态，防止沿用 3D 点采样导致大块纹素。End 恢复原采样状态，尊重 DoNotModifyRenderState/DoNotSaveState；共享路径同时影响 GL/Vulkan/Metal，本轮仅静态修改。
- Metal 用 RGBA8 存储提供 RGB8 逻辑语义，保证原版 X8R8G8B8 中间目标的 Alpha 为 1：RGB 上传补 1、清屏固定 1、管线屏蔽 Alpha 写入。原 PNG/RGBA 与源 Alpha 混合保留，管线缓存区分逻辑格式；修复半透明场景经 Filter 再次衰减的问题，尚未做 Mac 画面复测。
- 深度附件尺寸必须匹配目标；外部附件借用，不在Framebuffer销毁时释放；摘除外部附件恢复目标自己的深度。
- `BeginFrame`/`EndFrame`在提前返回或异常时也配对；原生提交失败保留具体错误信息。
- Vulkan acquire只在Success/Suboptimal后等待关联fence，不在Timeout/OutOfDate时等待未提交fence。
- Metal 为 shader 保留但顶点流未提供的属性绑定 `(0,0,0,1)` 常量流，不能依靠运行时 uniform 分支跳过顶点描述符校验。常量流槽 29、实际顶点槽 30，uniform 槽 0–28。流布局必须有属性引用；完整布局不配置槽 29，缺失布局才配置并绑定常量流，此条件与管线一起缓存。修复针对 Mac 实机反馈的缺失属性错误及无引用槽 29 原生断言，本轮仅静态修改，尚未复测。

## 验证

```sh
ALSOFT_DRIVERS=null xvfb-run -a sh tools/run-shader-regressions.sh
ALSOFT_DRIVERS=null xvfb-run -a sh tools/run-behavior-regressions.sh
sh tools/run-mac-regressions.sh
```

既有渲染9项、行为10项；不是每个用例都在所有API上执行。GL包括FX CPU/GPU数值对照和110项光照，Vulkan包括真实Sprite、透明合成、菜单图集、批量快照、深度和帧缓冲往返。Mac入口独立验证真实P/Invoke软件API驱动及真实shader转译，不能冒称Apple驱动光栅化验收。

Linux原生Wayland及X11的GL/Vulkan窗口实际运行通过；Vulkan菜单稳定阶段约60FPS、1提交/帧，关卡可暂停/恢复/退出。VK/GL菜单中心RGB均为(166,188,243)，非锁帧动画截图的整幅每通道平均绝对差小于1，不能证明所有场景逐像素一致。Mac没有把GPU加速当作API验证前提，但Apple系统运行、实体设备性能和完整关卡听感未由软件驱动证明。

诊断：`THSSS_DIAG_FPS=1` 约每秒输出实际 FPS、mainMs 和 renderer；Vulkan 保留当前帧提交/绘制数，Metal 输出区间内每帧平均 submits/draws/passes/waitMs/uploadKiB。waitMs 代表 CPU 同步等待，非纯 GPU 时间。读回诊断会额外同步，测性能时不要开启 THSSS_DIAG_PRESENT 或 THSSS_DIAG_VK_PIXELS。
