# 渲染后端（2026）

## 选择

Linux、Windows 默认 OpenGL，可选 Vulkan。 macOS 默认原生 Metal，可选 OpenGL。
`THSSS_RENDERER=gl|vk|metal` 显式选择后端。 未知值、设备或窗口表面不可用均明确报错。

Vulkan 使用 1.3 特性，窗口接入 Win32/X11/Wayland。
Metal 直接调用系统框架，不经 MoltenVK。
macOS 构建和运行见 [macOS][mac]。

[mac]: MACOS_2026.md

## 公共契约

`IRenderBackend` 管理资源、uniform、绘制、采样、 混合、深度、帧生命周期与呈现。
兼容层保持 640×480 游戏逻辑坐标， 窗口使用实际 framebuffer 像素尺寸。 缩放规则见[窗口缩放][window]。

[window]: WINDOW_SCALING_2026.md

OpenGL 由窗口层持有上下文并交换缓冲。 Vulkan/Metal 使用无 GL 上下文窗口，自行呈现。
兼容层包含 GL 专用优化和诊断分支。 非 GL 后端同样接收设备状态、光照与深度写入设置。

新后端需实现资源所有权、状态映射与原生窗口接入。 关卡逻辑、弹幕公式与游戏资源坐标保持不变。

## 批次、同步与资源

Sprite 在 Draw 时将变换写入齐次顶点。 同纹理的旋转/缩放可合批；纹理按对象身份比较。
绘制状态、目标或 FX 参数变化前提交待绘顶点。 文字更新或释放字形纹理前显式 Flush。

Vulkan 帧内录制并快照顶点及 uniform。 fence 完成后回收描述符和上传内存。
Metal 同目标与深度附件的连续绘制复用 encoder， 在呈现或帧结束集中提交。
读回、更新已用纹理、尺寸变化可要求提前同步。 一帧一次提交不是所有 API 场景的固定限制。

`Usage.RenderTarget` 使用渲染纹理工厂。 深度附件尺寸必须匹配目标。
外部附件为借用资源，不随 framebuffer 释放； 摘除外部附件后恢复目标自己的深度。
`BeginFrame` 与 `EndFrame` 在异常和提前返回时配对。

Vulkan acquire 只在 Success/Suboptimal 后
等待关联 fence；Timeout/OutOfDate 不等待未提交帧。

## 状态和格式

Sprite Begin 默认采用 Clamp 与线性过滤。 Flush 在绑定 FX 前应用当前设备采样状态。
End 恢复原状态，遵守以下标志：

- `DoNotModifyRenderState`：保留调用方状态
- `DoNotSaveState`：不恢复原状态

Metal 以 RGBA8 存储提供逻辑 RGB8： 上传及清屏的 Alpha 固定为 1，管线只写 RGB。
普通 PNG/RGBA 仍保留源 Alpha 和混合语义。 管线缓存区分 RGB 与 RGBA 逻辑格式。

Metal 为 shader 保留但顶点流缺失的属性 提供 `(0,0,0,1)` 常量流。
槽 29 用于缺失属性，槽 30 用于真实顶点， uniform 使用槽 0–28。
只有布局引用缺失属性时才配置并绑定常量流。

OpenGL uniform buffer 只有在完整更新内容相同 时才省略上传，普通绑定与索引绑定仍执行。
局部更新使缓存失效；顶点和索引不使用此缓存。 文字对象只缓存最近一份字形纹理。

## 着色器与诊断

Metal MSL 来自共享 GLSL 源的转换。 发布缓存齐全时不需要运行时转换工具。
Vulkan 仍需 `glslangValidator` 运行时转换。

`THSSS_DIAG_FPS=1` 输出 FPS、mainMs 和后端。 Vulkan 输出当前帧的提交及绘制数。
Metal 输出区间内每帧平均值： submits、draws、passes、waitMs、uploadKiB。
waitMs 是 CPU 同步等待，不是纯 GPU 执行时间。

性能测量保持 framebuffer、Replay 输入与配置一致。 关闭 `THSSS_DIAG_PRESENT` 和 `THSSS_DIAG_VK_PIXELS`，避免读回同步干扰。

## 验证

```sh
ALSOFT_DRIVERS=null xvfb-run -a \
  sh tools/run-shader-regressions.sh
ALSOFT_DRIVERS=null xvfb-run -a \
  sh tools/run-behavior-regressions.sh
sh tools/run-mac-regressions.sh
```

GL 检查 FX 数值、纹理上传和光照像素。 Vulkan 检查 Sprite、合成、深度及帧缓冲往返。
mac 软件驱动检查 P/Invoke 与着色器转换契约， 不替代 Apple GPU 光栅化、设备性能与完整关卡检查。
各 API 的用例覆盖不同，不承诺全场景逐像素一致。
