# 音频后端与平台构建

## 选择

| 系统 | 默认 | 依赖 |
|---|---|---|
| Linux | OpenAL | OpenAL Soft等支持AL_EXT_FLOAT32的实现 |
| Windows 10/11 | XAudio2 2.9 | 系统xaudio2_9.dll及COM MTA |
| macOS | CoreAudio | 系统AudioToolbox AudioQueue |

`THSSS_AUDIO_BACKEND=auto|openal|xaudio2|coreaudio`，默认auto。显式请求不支持平台的实现、未知名称或设备初始化失败均明确报错，不静默换后端。Windows/macOS可显式选OpenAL，但需提供该库。`THSSS_DIAG_AUDIO=1`输出实际选择。

## 编译边界

`THSSS_WINDOWS`/`THSSS_MACOS`根据目标RID定义：显式RuntimeIdentifier优先，未指定时使用NETCoreSdkRuntimeIdentifier。跨OS发行必须指定RID；普通net8.0/net10.0加win-x64不会自动定义SDK的WINDOWS常量。

Windows产物包含XAudio2、ole32与Win32窗口导入；macOS产物包含CoreAudio和Cocoa导入。Linux不包含这些原生音频类型。OpenAL仍是可选后端，不能把类型剔除当作所有非默认依赖均已删除。

## 播放契约

公共SlimDX.XAudio2兼容层保留循环、样本游标、逻辑队列、音量和输出矩阵。底层接收每个 voice 已处理输出矩阵的 float32 立体声块；不为每种平台重写循环点或音量曲线。

XAudio2 vtable：SetVolume12、DestroyVoice18、Start/Stop19/20、SubmitSourceBuffer21、FlushSourceBuffers22、GetState25。GetState返回void并接flags；Stop接flags和operationSet。PCM持有至已消费且公共层退休；UnqueueProcessed只退休一个块。Clear销毁旧voice后重建，Dispose不重建。没有Wine游标假进度补偿。

CoreAudio 后端只拥有一条持续运行的 AudioQueue，输出 44100Hz 交错 float32 PCM，复用三个各 256 帧的原生缓冲。各 voice 在软件中混合；其他采样率使用整数相位和线性插值转换到输出采样率。Play/Pause/Clear 不调用原生 Start/Pause/Stop/Reset；首次创建输出时 Start 一次，最终 Dispose 同步等待回调并释放缓冲。等待 Dispose 时不持有回调锁。原生 ABI 与验证说明见 [MACOS_2026.md](MACOS_2026.md)。

每段混音记录输出时间与源采样位置的映射，只有设备 SampleTime 越过相应范围才推进已播放游标；预取和回填回调本身不表示播放完成。Pause 冻结该 voice 的游标，Clear 清空该 voice 的 PCM 和映射，不重置共享输出或打断其他声音。已经交给系统的混音不能逐 voice 撤回，响应还受短缓冲及设备延迟影响；三个缓冲的 PCM 时长合计约 17.4ms，这不是实体设备总延迟的上界。

重复设置相同输出矩阵不重建 PCM；音效重播先 Stop/Flush，再设置新矩阵、Submit/Start，避免先为即将丢弃的旧声音重建队列。`THSSS_DIAG_INPUT=1` 输出各 voice 操作耗时与 source 编号（transport=shared），首次原生启动单独输出 `[AUDIO-OUTPUT]`。回调异常输出完整错误并保留原始堆栈，后续公共操作重抛，不把输出故障当成成功。

`thsss-perf-20261030-000848.log` 显示 Metal 已为一次提交/帧、等待通常约 1–2ms；射击期间却不断触发 AudioQueue Pause 约 32ms、Clear 约 11ms、Start 约 10ms，最慢统计区间降至 8.03 FPS。本轮据此改为共享持续输出，仅静态分析和修改，尚未构建、运行软件驱动或进行 Mac 听感/性能验证。

## 验证

```sh
sh tools/run-audio-regressions.sh
sh tools/run-mac-regressions.sh
```

OpenAL套件保留162项loopback像素外音频数值断言。后端公共契约覆盖有限/无限循环、暂停恢复、EOS、队列顺序、12voice并发、矩阵/音量独立、错误传播与释放。平台类型存在断言防止错误#if让后端消失。

Windows测试使用win-x64自包含EXE和隔离Wine前缀：

```sh
dotnet publish tests/testcase/AudioBackendCompatibility/AudioBackendCompatibility.csproj \
  -f net8.0 -r win-x64 --self-contained true -o /tmp/thsss-audio-win-test
mkdir -p /tmp/thsss-audio-wine
WINEPREFIX=/tmp/thsss-audio-wine \
THSSS_AUDIO_WINE_EXE=/tmp/thsss-audio-win-test/AudioBackendCompatibility.exe \
sh tools/run-audio-regressions.sh
```

音频DejaGnu默认3项，提供WindowsEXE后4项。CoreAudio 的 Linux 原生 API 软件驱动是单独的 mac 验收入口；当前断言覆盖共享队列、固定缓冲、PCM 快照、声道混合和音量、22050/44100/48000Hz 游标、暂停恢复、清空隔离及回调故障传播。驱动已更新但本轮未运行，不能将它称为 Apple 系统运行测试。Wine 不是微软本机驱动；设备热插拔、声卡听感及完整游戏图形/存档不由音频契约通过证明。
