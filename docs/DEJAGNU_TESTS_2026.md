# DejaGnu 测试驱动（2026）

## 结构

C# 夹具放在 `tests/testcase/<Suite>/`， 引用 `src/THSSS.csproj`。
进程驱动放在 `tests/dejagnu/<tool>/`。 公共过程位于 `tests/dejagnu/lib/`。

每个 toolkit 使用独立源目录。 `runtest --srcdir` 会执行该目录内全部 `.exp`。

## 回归入口

```sh
sh tools/run-shader-regressions.sh
sh tools/run-behavior-regressions.sh
sh tools/run-audio-regressions.sh
sh tools/run-mac-regressions.sh
sh tools/run-resize-regressions.sh
```

- shader：FX、像素、矩阵、模型与光照
- behavior：列表、音频、DES、存档、截图、输入、
  配置、构建警告、Skia 与 GL/Vulkan 窗口缩放
- audio：公共契约、非法后端与可选 Wine XAudio2
- mac：Metal/AudioQueue 软件 API 与着色器转译
- resize：GL/Vulkan 缩放、最小化和全屏往返

数学及 Replay 检查见[数学兼容][math]。

[math]: MATH_COMPATIBILITY_2026.md

## 依赖和环境

需要 .NET SDK、DejaGnu 和各后端所需原生库。 mac 软件驱动额外需要 C 编译器、
`glslangValidator` 与 `spirv-cross`。 缺少必要工具应报错，不静默跳过。

开发与回归入口使用 `THSSS_FRAMEWORK` 选择框架。 本地执行需要对应版本的运行时。

```sh
THSSS_FRAMEWORK=net8.0 \
  sh tools/run-mac-regressions.sh
```

Linux 无显示环境的窗口测试使用以下形式：

```sh
ALSOFT_DRIVERS=null xvfb-run -a \
  sh tools/run-behavior-regressions.sh
```

使用原生 Wayland 时可在桌面会话直接运行缩放入口。 数学 GUI 回归按自身入口隔离 X11/Xvfb 环境。

## 判定与数据隔离

1. 检查用例自己的 PASS 标记及预期退出码
2. 在驱动中显式设置超时，窗口用例使用独立预算
3. 将配置、Score、Replay 和截图输出放在临时目录

数值和像素断言保留在夹具中，驱动管理进程及超时。 预期负例的错误输出不单独代表整个用例失败。
任何测试都不得覆盖 `game/` 的玩家数据。

`THSSS_TEST_REPORT_DIR` 可指定报告目录：

```sh
THSSS_TEST_REPORT_DIR=/tmp/thsss-tests \
  sh tools/run-behavior-regressions.sh
```

## 工作目录与测试范围

渲染夹具按相对路径读取资源，shader 驱动切到 `src/`。 移动夹具时需检查仓库根定位与资源路径。
部分夹具使用 `AppContext.BaseDirectory` 向上定位。

GPU 测试应读回真实像素，音频测试应核对输出样本。 软件 API 驱动验证原生调用契约，不执行 Apple 光栅化。
编译、交叉发布和结构体布局检查不能替代设备运行。
