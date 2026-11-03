# 音频后端（2026）

## 后端选择

- Linux 默认 OpenAL，需要支持浮点 PCM 的实现
- Windows 10/11 默认系统 XAudio2 2.9
- macOS 默认 AudioToolbox AudioQueue

`THSSS_AUDIO_BACKEND` 接受以下值：

```text
auto
openal
xaudio2
coreaudio
```

默认 `auto`。Windows/macOS 可显式选择 OpenAL， 但需要提供相应原生库。 不支持的平台、未知值和设备初始化失败均明确报错。
`THSSS_DIAG_AUDIO=1` 输出实际后端。

## 平台编译

`THSSS_WINDOWS` 与 `THSSS_MACOS` 根据 RID 定义。 显式 `RuntimeIdentifier` 优先；
否则使用 `NETCoreSdkRuntimeIdentifier`。 跨系统发布必须指定 RID。 普通目标框架不会自动提供 SDK 的 `WINDOWS` 常量。

Windows 产物使用 XAudio2、ole32 与 Win32 导入； macOS 使用 CoreAudio 与 Cocoa 导入。
Linux 不包含这些平台原生音频类型。 OpenAL 是可选依赖，需按实际选择提供。

## 公共播放语义

SlimDX.XAudio2 兼容层管理循环、样本游标、队列、 音量与输出矩阵，向后端提供 float32 立体声块。
音量与声像独立；相同输出矩阵不触发 PCM 重建。

暂停保留播放位置，恢复不从零开始。 有限循环按样本边界结束，无限循环使用有界缓冲队列。 
音效重播按 Stop/Flush、设置矩阵、Submit/Start
的顺序处理，保留原游戏的音效请求频率。

## XAudio2

使用系统 `xaudio2_9.dll` 与 COM MTA。 原生 voice 方法槽位如下：

- SetVolume：12；DestroyVoice：18
- Start/Stop：19/20；SubmitSourceBuffer：21
- FlushSourceBuffers：22；GetState：25

GetState 返回 void 并接收 flags；
Stop 接收 flags 与 operationSet。
PCM 保留到消费完成且公共层退休。 UnqueueProcessed 每次只退休一个块。
Clear 销毁并重建 voice，Dispose 不重建。

## CoreAudio

后端共享一条持续运行的 44100Hz AudioQueue。 三个原生缓冲各容纳 256 帧交错 float32 PCM。
各 voice 软件混音，其他采样率按整数相位线性插值。

首次创建输出时启动一次；Play/Pause/Clear 只改变对应 voice，不启停共享设备队列。
Dispose 释放回调锁后同步等待并释放原生缓冲。

每段混音保存输出时间到源采样位置的映射。 设备 SampleTime 越过相应范围才推进播放游标；
预取和回填回调不表示声音已经播放。 暂停冻结 voice 游标，Clear 清空其 PCM 与映射。

已交给系统的混音不能逐 voice 撤回。 三个缓冲合计约 17.4ms PCM，不是总设备延迟上界。
回调异常保留原始堆栈，后续公共操作重抛。
原生 ABI 见 [macOS][mac]。

[mac]: MACOS_2026.md

## 诊断与验证

```sh
THSSS_DIAG_INPUT=1 THSSS_DIAG_FPS=1 \
  sh tools/run-game.sh
sh tools/run-audio-regressions.sh
sh tools/run-mac-regressions.sh
```

`[AUDIO-TIMING]` 输出 voice 操作耗时与编号。
`[AUDIO-OUTPUT]` 记录原生输出首次启动。
耗时包含锁等待，不能当作纯输入或纯设备耗时。

音频契约测试覆盖循环、暂停恢复、队列顺序、并发、 矩阵/音量独立、错误传播与资源释放。
OpenAL loopback 对照实际输出样本。

可选 Wine 测试先发布隔离的 Windows 测试程序：

```sh
(
  cd tests/testcase/AudioBackendCompatibility
  dotnet publish -f net8.0 -r win-x64 \
    --self-contained true \
    -o /tmp/thsss-audio-win-test
)
mkdir -p /tmp/thsss-audio-wine
WINEPREFIX=/tmp/thsss-audio-wine \
THSSS_AUDIO_WINE_EXE=\
/tmp/thsss-audio-win-test/\
AudioBackendCompatibility.exe \
  sh tools/run-audio-regressions.sh
```

CoreAudio 软件 API 驱动验证调用、混音和游标契约。 Wine 与软件驱动不能替代原生系统、实体声卡听感、
设备热插拔或完整游戏运行检查。
