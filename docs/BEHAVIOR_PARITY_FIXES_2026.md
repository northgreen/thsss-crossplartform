# 游戏行为兼容（2026）

## 输入与窗口

真实窗口事件统一进入 `WindowInputBridge`。 `UseDirectInput=0` 转发 Control 事件；
值为 1 时使用输入快照。两种模式均读取配置键名。 Enter 与 KeypadEnter 按转换后的键聚合。

失焦触发原 Activated/Deactivate 流程， 释放事件和快照中的按键，忽略后台新按下。
键盘与手柄可混用，设备连接、轴和按钮通过兼容层。 Linux 按 capabilities 过滤伪手柄设备。

ClientSize、初始配置、F8 与光标状态转发真实窗口。 窗口关闭执行保存、录像关闭和资源释放。
加载线程停止超时抛 `TimeoutException`， 取消关闭，不保存或释放仍被工作线程使用的资源。

同一数据根使用文件锁防止多实例并发写。 不同数据根可分别运行。
错误通过 stderr 输出标题及原因。 配置解析失败停止启动，不保存不完整参数。

## 列表遍历与随机顺序

原 ForEach 调用使用 `ListCompat.LegacyItems`。 遍历每轮读取实时长度并按索引访问：
删除当前元素会跳过左移项，尾部追加可在同轮访问。 不能替换为快照或使用检查版本号的枚举器。

视觉对象更新可能消耗关卡共享 RNG。 排除纯绘制计算时仍需保留随机调用及对象更新顺序。
数学和 RNG 规则见[数学兼容][math]。

[math]: MATH_COMPATIBILITY_2026.md

## 存档与 Replay

存档保留 DES 密钥、IV、CBC、填充和十六进制容器。 文本先严格尝试 UTF-8，再尝试 CP936，
保存时保留载入编码，不使用替代字符吞掉错误。 无法无歧义识别同时合法的两种编码时采用 UTF-8。

载入先解析到临时 PlayerData，全部成功后提交。 失败设置 LoadError 并保留原状态，阻止自动 Save。
保存先完成编码，再 Flush、原子替换并保留 `.bak`。 坏档禁止自动覆盖是明确的数据保护行为。

Replay 使用固定文化写数字。 读取接受原写入语法中的单逗号小数，含三位小数；
混合分隔符、非有限值、非法关卡和截断记录明确拒绝。 结局标记 `LastStage=All` 有效。
意外 EOF 关闭流、清键、报错并返回标题，停止模拟。 开发专用 Stage_test 不属于回放菜单支持范围。

测试必须在临时目录读写 Score、Replay 与截图。 同版本录制/回放一致不代表所有原版录像兼容。

## 字体、截图与音频

Font 默认构造器使用 point，按 96 DPI 转像素； 显式 GraphicsUnit 与原生像素高度构造器保留单位。
测量、换行与绘制共用布局和回退规则。 字体与编码见[字体与图像][fonts]。

[fonts]: FONTS_AND_IMAGES_2026.md

游戏内 P 键只排队一次截图请求， 在本帧绘制后、交换前捕获，默认保存 JPEG。
按实际内容区域读回，再还原到 640×480。 读回恢复 framebuffer、readbuffer、PBO 与 pack 状态。
窗口缩放规则见[窗口缩放][window]。

[window]: WINDOW_SCALING_2026.md

BGM 保留循环点，暂停恢复延续原样本游标。 音量与声像独立；音效重播不重建即将清除的旧 PCM。
文件名 WAV 构造器复制后释放输入； Stream 重载不接管调用方流。
播放细节见[音频后端][audio]。

[audio]: AUDIO_BACKENDS.md

## 帧节奏

窗口负责等待，保留原逻辑帧比与 fixed-FPS 滞后上限。 按配置设置 VSync；窗口模式 interval 大于 1
仍使用软件限速，全屏可使用 swap interval 2/3。

`FinishFrameTiming` 按累计欠帧等待 deadline， 执行 16.62ms 采样循环，再按 DelayTolerance 补偿。
VSync 且 interval 为 1 或全屏时提前返回。 游戏自行计时时不叠加 GLFW FramesPerSecond 限速。
非推进回调不计为新游戏帧。

## 验证方式与范围

```sh
ALSOFT_DRIVERS=null xvfb-run -a \
  sh tools/run-behavior-regressions.sh
ALSOFT_DRIVERS=null xvfb-run -a \
  sh tools/run-shader-regressions.sh
```

行为入口包含列表、音频、DES、存档、文本截图、 窗口输入、配置、构建警告、Skia 与窗口缩放检查。
像素、音频样本、真实进程退出和临时文件为观察依据。 自动焦点、节拍或软件设备测试不能替代真人设备体验。

设备热插拔、手柄手感、显示器 VSync 和扬声器听感 需要相应硬件运行检查。
有限行为用例不构成所有关卡、所有旧录像的兼容证明。
