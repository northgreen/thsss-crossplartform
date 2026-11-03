# CPU 数学兼容（2026）

## 范围与构建模式

影响游玩、判定和 Replay 的路径使用 LegacyFloat、 旧 MyRandom 与 Fody 编译期转换。
绘制、相机和纯渲染矩阵不在数值转换范围。 视觉对象更新仍保留共享 RNG 的调用及顺序。

`THSSSMathMode` 是编译参数，支持以下模式：

- auto：启用兼容，按目标选择后端
- software：启用兼容并强制软件后端
- off：使用普通 .NET 数学，不保证旧录像兼容

```sh
dotnet build src/THSSS.csproj \
  -c Release -f net8.0 \
  -p:THSSSMathMode=software
perl tools/make-dist.pl --rid linux-x64 \
  --math-mode software
```

默认 auto：支持的 x64 目标优先硬件 x87， ARM64 使用不依赖宿主 libm 的软件实现。
运行时 `THSSS_MATH_BACKEND=auto|software|x87` 可用于诊断对照。
software 构建拒绝 x87 环境覆盖； 未知值和不支持的硬件选择明确报错，不静默降级。
启动日志用 `[MATH]` 输出实际后端和精度。

off 不织入 IL，不包含 LegacyFloat 或数学原生库。 模式不是运行时热切换。
`THSSS.math-mode.json` 绑定编译模式及最终 DLL 哈希。 切换模式需正常 restore/build，
不能复用其他模式的 no-restore/no-build 产物。

## 精度与格式

兼容目标为 x86 CLR 2.0.50727.9179、 原版 THSSS 1.0.0.1 与 SlimDX 2.0.13.43。
D3D9 未指定 FpuPreserve 的设备初始化语义 要求游戏线程采用 24 位有效数字、就近舍入。
初始化前及新托管线程采用 53 位状态。 精度是线程状态，不能作为全局常量处理。

24 位算术不等于把 double 存储全部换成 float。 寄存器指数范围、超越函数及落盘边界分别保留。
LegacyFloat 通过 16 字节 ABI 传递扩展值。 显式 float/double 转换按原 binary32/binary64
边界落盘，不把 C# 局部声明视为必然存储。

硬件使用 x87 加减乘除及 FSIN/FCOS/FPATAN/FSQRT。
以 FNSAVE/FRSTOR 完整保存恢复调用者栈与状态， 不永久改变宿主线程，不改变 MXCSR。

两后端共享整数编解码器。 FromSingle/FromDouble 逐位展开，保留符号、
次正规数和旧 NaN quiet/payload 规则。 ToSingle/ToDouble 仅对精确可表示的规范值
使用整数快速编码；其余交给完整原生 codec。 格式搬运不改用宿主浮点算术。

软件内核使用 CLZ、128 位整数和 ThinLTO， 不启用 fast-math，不合并独立 Sin/Cos。
不同 CPU 的超越函数仍需同一旧基准校验。

## 编译期转换

`tools/GameplayMath.Fody/` 按游戏逻辑根、 派生类型及实际生成的回调选择方法。
未知 IL 或含混回调直接失败。 绘制与 IEnvironment 相机/绘制契约不重写。

清单位于以下构建目录：

```text
src/obj/<配置>/<框架>[/<RID>]/
  GameplayMath.manifest.json
```

清单记录作用域、类型、转换与 RNG 顺序。 LegacyFloat 实现位于 `src/Platform/Compat/`，
原生内核位于 `native/LegacyMath/`。

BaseObject.Vx/Vy 使用内部扩展返回通道。 setter 分量在 sqrt 后、速度 setter 前落盘。
Debug 返回槽单独规范化，未知 getter 结构拒绝。

游戏向量恢复 SlimDX 的长度与归一化顺序： 分量转 double，平方求和后 sqrt，长度转 float，
先舍入 float 倒数，再乘各分量。 绘制共用 MathTypes 与纯矩阵 API 不参与替换。

MyRandom 保留整数状态、浮点缩放、参数异常、 ranValue 与各接口逐操作舍入。
Execution.region 使用旧七位有效数字文本， 保留旧中点舍入和负零，不以现代 G7 替代。

## 几何、移动与弹幕数值块

距离、圆形和椭圆判定作为完整数值块执行。 保留坐标读取顺序、sqrt 参数及公有返回的
binary64 边界、Region 的 binary32 转换和严格比较。 虚派发、候选顺序和命中算法保持不变。
硬件块只保存恢复一次 FPU 状态。

圆形整数粗筛只拒绝明确远离的候选。 守卫要求稳定且有限的坐标及受限非负半径，
使用外扩整数上界与 16 像素余量。 守卫失败回退精确图；粗筛不能直接接受命中，
不能用平方距离替代原判定。

椭圆保留方向加角度、距离加范围和三角扩展结果。 软件路径在 Cos 项已大于等于 1 或为 NaN 时
可提前拒绝；各项舍入、半轴边界及最终相加顺序不变。 物理更新按窗口线程顺序执行，
不承诺任意并发位置写入下的穿插时序等价。

移动块保留 MovePoint 分支、两次 Position getter、 四次原坐标及六次边界偏移读取。
最终 setter 分量独立落盘到单精度。 原坐标加偏移再减偏移不能代数消除。
块返回 OriginalPosition，不重复调用 Position setter。

StraightLaser.HitCheck 每候选点使用独立数值块， 保留点循环端点、早退和四次目标位置捕获。
sqrt 参数及两侧范围乘积各有 binary64 边界； sqrt 返回保留扩展精度，不复用圆形返回落盘块。
整数粗筛还要求四次目标坐标一致和非负范围因子。

角度更新保留 degree getter、增量、setter 的 逐次 binary64 边界与原归一化加减循环。
脚本加速度保留扩展角度，Cos/Sin 各自保留 binary64 参数、binary32 返回与最终分量存储。
不跳过零值，不改用合并三角函数。

## 生产回归

需要 .NET SDK、Python 3、Clang/LLVM 与 Fody 还原。 从仓库根执行两个配置及实际游戏回归：

```sh
sh tools/run-gameplay-math-regressions.sh \
  --configuration Debug
sh tools/run-gameplay-math-regressions.sh \
  --configuration Release
python3 \
  tests/testcase/GameplayRuntime/run_live.py \
  --configuration Debug --frames 900 --timeout 300
```

回归入口可加 `--backend auto|software|x87`。 比较实际生产方法、运动/判定、脚本、文本、
资产解析与完整 RNG 状态，不只比较屏幕文本。

实际游戏检查使用临时数据和独立录制/回放进程， 读取真实键流，逐帧比较逻辑字段与完整 RNG 状态，
包含暂停冻结、恢复、移动与射击。 GUI 隔离需移除继承的 Wayland 环境并显式选择 X11，
确认私有 Xvfb 上窗口属于游戏 PID。

资产副本需保留控制器的实际枚举顺序。 相同文件内容不一定意味着相同 Replay 输入环境。
不能通过生产排序掩盖测试副本的顺序差异。

Linux x64 原生状态与性能检查：

```sh
python3 \
  tests/testcase/GameplayMath/check_backends.py \
  src/bin/Release/net8.0/libthsss_legacy_math.so
```

检查原始位、非法选择、满 x87 栈、异常状态恢复， 并输出运算与格式转换的热循环中位数。
原生微基准不能直接当作实际游戏帧率提升。 性能对照需使用相同 framebuffer、Replay 键偏移
和配置，非推进回调不计为新游戏帧。

## 旧环境基准

独立原生 x87 预言机与采集工具位于 `tests/testcase/MathBaseline/`。
生产程序集检查位于 `tests/testcase/GameplayMath/`。 冻结旧方法的图与机器码检查用于验证优化等价，
不代替真实 CLR2 基准。

隔离候选解法对照入口：

```sh
sh tests/testcase/MathBaseline/verify.sh
```

需要 .NET 8、Python 3 与 GCC。 两个精度状态均零差异且输出
`MATH_BASELINE_SOLUTIONS_PASS` 才算通过。 该入口本身不证明生产程序集兼容。

重新采集使用同目录 `capture-legacy.ps1`。 需 NetFx3 x86 CLR2、原版 THSSS/SlimDX、
x86 D3DX9 及可创建 D3D9 设备的交互桌面。 无法创建设备时明确失败，不跳过设备后精度样本。
采集只写临时及显式输出目录，不访问玩家数据。 黄金压缩输出只能由真实旧环境重新采集，
不能用当前实现结果覆盖。

## 分发与兼容边界

原生库支持 Linux/Windows/macOS 的 x64/ARM64。 分发必须包含可替换动态库、完整对应源码与许可证，
具体见 `native/LegacyMath/NOTICE.md`。 vendor 文件按许可清单校验原始字节，不能归一化换行。

有限黄金样本、冻结图等价或同版本 Replay 一致 不构成全部历史录像、全部旧 JIT、所有极值输入
及所有 CPU 微架构的普遍兼容保证。 纯渲染矩阵、GPU 数学与未调用 API 不在转换范围。
交叉构建和 QEMU 检查不替代实体设备性能检查。
