# 根本目标

移植原版thsss（东方夏夜祭）到更现代的dotnet并且适配权平台

任何时候要尽可能贴合原版thsss

禁止将历史或者说决策过程写入任何文档

README.md是面向普通读者的入口，只需要介绍项目和基本的比如说如何看到效果即可

文档一行不宜过短但也禁止超过50字符

## 工作区入口

- `src/THSSS.csproj` 是唯一当前开发项目；`reference/` 下的原版、反编译和早期尝试只用于对照，不作为启动入口。
- `game/` 是运行数据根：资产、配置、Score/Replay/ScreenShot一起保留。测试必须复制/使用临时数据，不得覆盖这里的玩家数据。
- 从根运行 `sh tools/run-game.sh`；SDK唯一配置是根 `global.json`。回归入口为 `tools/run-shader-regressions.sh`、`tools/run-behavior-regressions.sh`、`tools/run-audio-regressions.sh`、`tools/run-resize-regressions.sh`。
- C# 测试在 `tests/`，DejaGnu 驱动在 `tests/dejagnu/`；测试必须使用临时数据。
- 当前状态与约定集中在 `docs/`，`docs/README.md` 是索引。只保留有效内容，不保留修复前快照。
- 游戏数值兼容入口：`sh tools/run-gameplay-math-regressions.sh --configuration Debug`（同时验收 Release）；真实输入/暂停/Replay 为 `python3 tests/testcase/GameplayRuntime/run_live.py --configuration Debug`，始终使用临时数据。Fody 只重写游戏逻辑，构建需要 Python 3 和 Clang/LLVM；分发必须带 `native/LegacyMath/` 完整对应源码与许可。详见 `docs/MATH_COMPATIBILITY_2026.md`。
- 数值后端默认 x64 使用硬件 x87、ARM64 使用软件；`THSSS_MATH_BACKEND=auto|software|x87` 为显式诊断覆盖，测试可传 `--backend`。两后端必须使用同一旧基准并保持16字节扩展ABI，硬件运算须保存/恢复完整x87状态，格式搬运共享整数编解码器；不得以切硬件为由改变舍入边界或忽略性能实测。
- 玩家入口 `tools/run-game.sh` 使用 Release，避免默认 Debug 的游玩数值开销；调试构建由显式 `dotnet run -c Debug --project src/THSSS.csproj` 或测试入口选择。性能比较必须使用同一实际 framebuffer、Replay 键偏移和配置，非推进回调不能当作新游戏帧。
