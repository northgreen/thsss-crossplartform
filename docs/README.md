# 文档索引（2026）

## 平台与运行

- [macOS][mac]：运行、打包与原生接口
- [Windows][win]：资源键与启动要求
- [音频后端][audio]：后端选择和播放语义
- [托管 DES][des]：Wine/Proton 可选构建

[mac]: MACOS_2026.md
[win]: WINDOWS_STARTUP_FIX_2026.md
[audio]: AUDIO_BACKENDS.md
[des]: MANAGED_DES_FALLBACK_2026.md

## 画面与游戏行为

- [渲染后端][render]：选择、资源和同步
- [Shader 与光照][shader]：FX 与固定管线
- [窗口缩放][window]：4:3、DPI 与截图
- [字体与图像][fonts]：字体、像素与依赖隔离
- [行为兼容][behavior]：输入、存档与帧节奏
- [数学兼容][math]：数值模式、舍入与回归

[render]: RENDER_BACKEND_STATUS_2026.md
[shader]: SHADER_FIXES_2026.md
[window]: WINDOW_SCALING_2026.md
[fonts]: FONTS_AND_IMAGES_2026.md
[behavior]: BEHAVIOR_PARITY_FIXES_2026.md
[math]: MATH_COMPATIBILITY_2026.md

## 测试入口

[DejaGnu 驱动][tests]说明依赖、判定和数据隔离。

[tests]: DEJAGNU_TESTS_2026.md

```sh
sh tools/run-shader-regressions.sh
sh tools/run-behavior-regressions.sh
sh tools/run-audio-regressions.sh
sh tools/run-resize-regressions.sh
sh tools/run-mac-regressions.sh
```

数学回归见[数学兼容][math]。
测试必须使用临时数据，不覆盖 `game/` 的玩家文件。
Linux 无显示环境可给窗口测试加以下前缀：

```sh
ALSOFT_DRIVERS=null xvfb-run -a \
  sh tools/run-behavior-regressions.sh
```

## 原作资料

`角色设定&ExStory.txt` 保留原作内容与编码。
