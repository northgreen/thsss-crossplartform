# 窗口缩放与 DPI（2026）

## 使用

运行游戏后拖动窗口边缘，可使用非整数倍尺寸。 F8 在窗口与全屏状态间切换。

```sh
sh tools/run-game.sh
THSSS_RENDERER=vk sh tools/run-game.sh
```

游戏坐标、碰撞、HUD、对话及关卡离屏目标 保持 640×480。字体随整体画面等比缩放，
不改变字号或重新排版，不提高原贴图分辨率。

全屏按显示器分辨率呈现，内容居中保持 4:3， 余下区域留黑边，不裁切游戏画面。
P 截图从物理内容区域读回，再还原到 640×480。 Sprite、3D、截图与合成共用 PresentationGeometry.Fit。

## 窗口比例与 DPI

GLFW 比例提示约束客户区为 4:3，不含标题栏。 X11 在帧边界校正尺寸，不在 Resize 回调递归设置。
Wayland 接受合成器分配的外框尺寸， 不与合成器反复修改窗口；内容在实际窗口内等比适配。
例如 1000×650 容器显示 866×650 内容与左右黑边。 不修改用户窗口管理器的全局策略。

窗口坐标与 framebuffer 像素尺寸分开。 每帧比较真实 FramebufferSize，改变时才通知后端。
窗口坐标不变但 DPI 改变时同样更新输出。 Windows apphost 声明 PerMonitorV2/PerMonitor DPI。

最小化或零像素尺寸跳过绘制与呈现。 恢复后使用当前像素尺寸，不重建关卡资源或存档。
GL 更新输出尺寸；Vulkan 安全等待后重建交换链。 默认目标的深度按实际尺寸匹配，
旧 640×480 附件不能覆盖任意窗口尺寸。

Vulkan 按 surface 能力和 extent 判断重建。 零 extent 推迟到后续帧，不因 Suboptimal 每帧重建。
呈现使用线性 blit，已有黑边的物理 backbuffer 不重复缩放，逻辑目标按 4:3 适配。

## 验证

```sh
ALSOFT_DRIVERS=null xvfb-run -a \
  sh tools/run-resize-regressions.sh
ALSOFT_DRIVERS=null xvfb-run -a \
  sh tools/run-shader-regressions.sh
ALSOFT_DRIVERS=null xvfb-run -a \
  sh tools/run-behavior-regressions.sh
```

有桌面会话时可去掉 Xvfb 前缀，验证原生窗口协议。 测试创建临时配置、Score、Replay 和 ScreenShot，
资产只读链接，不覆盖玩家文件。

缩放夹具检查 GL/Vulkan 的以下行为：

1. 多尺寸、非 4:3、最小化恢复与全屏往返
2. 非对称四角图案、方向、内容边界和黑边
3. DPI 像素变化只通知一次，不改变逻辑坐标
4. 稳定尺寸不持续重建 Vulkan 交换链

全屏测试调用游戏自身 ToggleFullScreen。 窗口系统可将该模式报告为 Maximized，
实际 framebuffer 输出不等于独占全屏能力。

模拟 DPI 变化不替代跨不同 DPI 显示器拖动检查。 Linux 窗口测试不替代 Windows/macOS 原生运行。

