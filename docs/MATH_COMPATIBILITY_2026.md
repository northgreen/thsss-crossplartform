# CPU 数学兼容性（2026）

影响游玩的生产数值路径使用 `LegacyFloat` 扩展数值、旧 `MyRandom` 和 Fody 编译期转换。
原生内核位于 `native/LegacyMath/`：支持的 x64 目标默认使用硬件 x87 算术及超越函数，ARM64 使用可移植软件后端，不依赖宿主 libm；
绘制、相机和纯渲染矩阵不在修复范围。`tests/testcase/MathBaseline/` 保留独立原生 x87 预言机，
`tests/testcase/GameplayMath/` 验证实际生产程序集。有限黄金样本及同版本 Replay 一致性不代表所有历史 Replay 已证明兼容。

## 生产实现与验收入口

- `tools/GameplayMath.Fody/` 按游戏逻辑根及其派生类型、实际生成回调选择方法。输出 `src/obj/<配置>/<框架>[/<RID>]/GameplayMath.manifest.json`，保留作用域、类型、转换及 RNG 顺序；未知 IL/含混回调明确失败。绘制及 `IEnvironment` 相机/绘制契约不重写；视觉对象更新仍保留共享 RNG 消耗
- `src/Platform/Compat/LegacyFloat*.cs` 显式运输 16 字节扩展值，逐操作选择 24/53 位并保留存储边界。`Game_Main` 初始化后进入 24 位，新托管线程默认 53 位；硬件操作临时设置并完整恢复调用者 x87 状态，软件操作不触碰它，均不改变 MXCSR
- `BaseObject.Vx/Vy` 使用内部扩展返回通道，setter 的 component 在 sqrt 后、速度 setter 前按真实旧 JIT 落盘。Debug 编译器返回槽单独规范化，未知 getter 结构仍拒绝
- `MyRandom` 自行保持旧整数状态、浮点缩放、参数异常及 `ranValue`；`EventGroup` 的 `Execution.region` 使用旧七位有效数字文本，包含旧中点舍入和负零行为。现代 `G7` 的 ties-to-even 不能替代它
- `LegacyFloat.Vectors` 只替换实际游戏调用的 Vector2 运算；共享 `MathTypes` 的绘制运算和纯矩阵 API 保持原状
- `LegacyFloat.Geometry` 将距离、圆形与椭圆命中表达式作为完整数值块执行，保留坐标读取顺序、sqrt 前与公有方法返回的 binary64 边界、Region 的 binary32 转换及严格比较；虚派发、候选顺序和命中算法不变。每个硬件块只保存/恢复一次 FPU 状态
- 圆形数值块内的保守整数粗筛只提前拒绝明确远离的候选：在稳定坐标、有限且受限的非负半径域，用外扩的整数上界加16像素余量。任一守卫不满足就回退原精确图；不改变候选顺序，不直接接受命中，不使用平方距离替换
- 椭圆块内的方向加角度、距离加目标范围与三角函数结果保持扩展中间值；仅在原 IL 的显式转换、Math 参数及公有返回处落盘，不把 C# 的 double 局部声明当成存储边界。实际物理更新由窗口线程顺序执行；标量捕获不承诺任意并发位置写入与原计算间穿插时序等价
- 移动块保留 `MovePoint` 分支、两次完整 `Position` getter、四次原坐标读取、六次边界偏移读取，以及最终 setter 的独立单精度落盘。原坐标加偏移再减偏移不能代数消除；块返回 `OriginalPosition`，不再重复调用 `Position` setter
- `Bullet_StraightLaser.HitCheck` 的每个候选点使用独立激光数值块，保留点循环端点、命中早退及四次目标位置捕获。sqrt 参数与两侧范围乘积各有一次 binary64 边界，sqrt 返回本身保持扩展精度；不能复用带返回落盘的圆形块。`tests/testcase/LaserCompatibility/` 对照冻结的实际旧方法，`NativeChecks/` 校验原语图、FPU 状态与机器码；这是现代实现优化等价验证，不是新增真实 CLR2 黄金
- 激光点块的整数粗筛仅在四次目标坐标捕获相同、有限范围及非负范围因子满足守卫时提前拒绝远点；原点索引、严格命中比较和近点精确图保持不变。资产内容相同不代表 Replay 输入相同：六面按目录枚举顺序创建控制器，测试须复用原资产树并验证实际枚举顺序，不能通过生产排序掩盖测试副本改变顺序的问题。
- `native_control.cpp` 合并公共角度增量及弹幕脚本加速度计算。角度保留 degree getter、增量与 setter 的逐次 binary64 边界，归一化仍使用原 setter 的加减循环；加速度保留扩展角度、Cos/Sin 各自的 binary64 参数与 binary32 返回和最终分量存储，不跳过零值或改用合并三角函数。`DegreeCompatibility`、`AccelerationCompatibility` 分别冻结实际旧方法并校验原语图和宿主状态；活动场景所有权检查覆盖角速度、脚本加速度输入和 Ax/Ay 写入。完整六面优化等价不等于选面录像的原始末态完全匹配，亦不保证稳定60帧
- `FromSingle/FromDouble` 使用逐位整数展开，精确保留符号、次正规数及旧 NaN quiet/payload 规则；`ToSingle/ToDouble` 只对已精确可表示的规范值走整数编码，所有舍入、非规范 ext80、NaN/无穷与越界都回退原 native codec。算术仍走选定 x87/软件后端，没有借机改成宿主浮点计算或忽略 GC 转换约束
- 编译参数 `THSSSMathMode=auto|software|off` 决定是否包含兼容路径。默认 auto 保留自动后端及环境覆盖；software 固定软件实现并拒绝硬件环境覆盖；off 使用普通 .NET 原表达式与 Random 子类，既不织入也不包含数学 native 库及 LegacyFloat 类型，不接受旧 CLR 黄金或旧 Replay 一致性保证。三态不是运行时热切换，发布记录绑定最终 DLL 哈希
- 软件内核启用现成的 CLZ/128 位整数原语与 ThinLTO，消除数学块内部重复包装调用；保持导出 ABI、旧舍入和独立 Sin/Cos，不使用 fast-math。软件椭圆判定在原 Cos 项已为 >=1 或 NaN 时提前返回未命中：另一平方项只能非负或 NaN，原有序比较必为假。只移动独立项的求值，保留每项的逐次舍入、N64 半轴边界及最终 left+right 顺序，硬件表达式不变

执行生产回归（需要 .NET、Python 3 和 Clang/LLVM；源码构建还需要 Fody 包还原）：

```sh
sh tools/run-gameplay-math-regressions.sh --configuration Debug
sh tools/run-gameplay-math-regressions.sh --configuration Release
python3 tests/testcase/GameplayRuntime/run_live.py --configuration Debug --frames 900 --timeout 300
```

默认 `THSSS_MATH_BACKEND=auto`（不设置也相同）：x64 优先 x87，ARM64 使用 software。
启动日志打印 `[MATH] backend=x87 precision=24` 或 `backend=software`。
`THSSS_MATH_BACKEND=software|x87` 只用于显式诊断/回归对照；未知值报错，不支持 x87 的目标强制 x87 也会报错，不静默降级。
回归入口和实际游戏测试另支持 `--backend auto|software|x87`。

硬件后端使用真实 `FSIN/FCOS/FPATAN/FSQRT` 及 x87 加减乘除，结果保持扩展格式。
每次硬件运算以 `FNSAVE/FRSTOR` 保存恢复完整栈和控制/状态；并非永久把宿主线程改成旧 FPU 模式。
float/double 与扩展格式之间的装载/落盘共用经过验证的整数编解码器，避免单纯格式搬运也保存整套 FPU 状态。
独立软件入口保留用于对照，不因默认硬件分派而改变含义。

Linux x64 的状态与原生性能测试：

```sh
python3 tests/testcase/GameplayMath/check_backends.py src/bin/Release/net8.0/libthsss_legacy_math.so
```

它检查两后端的原始位、非法参数/选择错误、满 x87 栈和异常状态恢复，并分别打印原生运算/格式转换热循环中位数。
原生微基准与实际游戏/追踪耗时分别验收，不把原生加速比直接当作帧率提升；不同 CPU 的超越函数仍需旧基准对照。

2026-10-31，Linux x64/i7-10750H 实测：原生 Sin→Mul→Add 的逐调用中位数从约197ns降至122ns（约1.6倍）；
共享整数格式转换约7–8ns，未使用昂贵的完整FPU保存做单纯格式搬运。
两后端 Release 各录制/回放900帧，跨后端的全部逻辑/RNG字段和1800字节Replay键流完全相同。
该软件渲染/完整追踪场景的回放平均帧间隔为软件27.76ms、x87为27.39ms（约1.3%改善），录制耗时基本相当；
不能将原生微基准的加速比当作实际游戏帧率提升或稳定60FPS保证。默认Debug自动x87也完成900帧门禁。

实际GUI回归必须移除继承的Wayland显示环境并显式选择X11，再验证私有Xvfb上存在游戏PID所属窗口；
否则GLFW可绕过Xvfb打开真实桌面窗口，用户焦点变化会触发真实自动暂停，不能把这种失败当作数值不兼容或绕过暂停断言。

前两项分别对照两种精度共 **434,962 行**真实游戏方法、逐帧运动/判定、脚本及随机状态/错误链；
另验 **74,172 项**标量/向量/转换、**161,232 行**旧 Single 文本和 **10,056 项**实际资产数值解析。
现有 937 个弹幕资产的 1,257 个数值字符串在两文化/两状态中未见解析差异，不推广到任意外部输入。

实际游戏测试使用新复制的临时数据和两个独立进程，读取真实 Replay 键流并逐帧比较逻辑字段与完整 RNG 状态；
测试包含暂停冻结、恢复、移动和射击。它验证同版本录制/回放，不替代原版全关卡历史 Replay 交叉验证。
软件绘制、追踪及受限环境的耗时不能直接视为实际硬件帧率。

原生库支持 Linux/Windows/macOS 的 x64/ARM64 六 RID；SDK8 和 SDK10 有构建/发布验证。
Linux ARM64 的 .NET 验证使用 QEMU；Windows x64 的数值检查使用真实 Windows VM。未声称实测 Apple 硬件或 Windows ARM64。
分发包含可替换动态库、完整对应源码及许可证，见 `native/LegacyMath/NOTICE.md`。

## 基准身份与精度状态

| 项目 | 实际环境 |
|---|---|
| Windows | 10.0.26200.8655，QEMU x86_64/KVM；运行 32 位探针 |
| 原版 CLR | `2.0.50727.9179`，`mscorlib 2.0.0.0`；来自 Windows NetFx3 |
| 原版程序集 | `THSSS 1.0.0.1`，MVID `1974659d-528f-42fe-8ba7-43fc949ab4ac`，PE 明确要求 32 位 |
| 原版 SlimDX | `2.0.13.43`，直接加载 `reference/original/SlimDX.dll` |
| 当前环境 | Linux x64，.NET `8.0.32`；当前项目实际编译的 `THSSS.dll` |
| D3D9 设备 | 交互式 Windows 会话，Microsoft Basic Render Driver，`HardwareVertexProcessing` |

原版 `reference/decompiled/Shooting/Game_Main.cs:181–221` 创建 D3D9 设备时只指定顶点处理方式，没有 `FpuPreserve`。
按同样标志创建真实 D3D9 设备，线程浮点控制字实际从 `0x0009001F` 变为 `0x000A001F`：

- 创建前：x87 **53 位有效数字精度**、就近舍入
- 创建后：x87 **24 位有效数字精度**、就近舍入

这不是将 `double` 存储类型改成 `float`。寄存器格式、指数范围、超越函数指令及落盘时机仍须分别处理。
不能用“旧版一直保留 80 位精度”概括，也不能将设备创建前的控制台结果直接当成游戏线程的运行状态。

设备创建前后均保留基准。同一进程在创建 D3D 后新建的托管线程实测为 `0x0009001F`（53 位），创建设备的主线程仍为 24 位。因此不能把精度当作全局常量；原版还在设备初始化后创建资源加载线程。这里不代表所有线程、历史 CLR 补丁和硬件驱动的环境。

## 相同输入与观察范围

`inputs.bin` 包含 4,096 组小端 `double angle, double value, float x, float y`，两端读取同一份字节，不各自生成近似输入。其 SHA-256 为：

```text
6bd574af15179cca05f8f249ae0bd8218b22a82ddd01c0b3cd506c00b5a2ba6d
```

`Probe.cs` 记录数学函数、游戏移动公式、SlimDX 向量及三个矩阵分量，并记录 16 条固定方向轨迹各 10,000 帧的两个坐标，共 381,446 个位模式。`ActualProbe.cs` 直接加载原版/当前游戏程序集，用未运行构造器的 `BaseObject` 调用真实 `GetDistance`、`GetDirection`、`Vx`、`Vy`，另有 16,384 个结果。`RandomProbe.cs` 对实际游戏 `MyRandom(0)` 执行 10,000 轮固定混合调用，另有 80,000 项。每种精度状态共 477,830 项；不调用游戏入口、不读取或写入玩家数据。

原版 SHA-256：

```text
THSSS.exe  8b2b3bf4a3df620101a2edf32a406a49f71cb985f78e3b0ceff1819826bb0d9e
SlimDX.dll 41be5dc48845f4f962abf4904f546eb905d4276baac1492e61ea5b2b17ad64bb
```

设备创建前基准的 381,446 项，以及重启虚拟机后重新创建 D3D 得到的 397,830 项数学/真实游戏方法结果，重复采集完全一致。所有比较使用浮点原始位模式，不用屏幕上四舍五入后的十进制文本判等。

## 已定位的差异

### 游戏方法：中间精度与舍入边界

`src/Shooting/BaseObject.cs:1091、1101` 的方向、距离计算直接影响游戏逻辑。与设备创建后的原版比较：

| 实际方法 | 不同位模式 / 样本数 |
|---|---:|
| `GetDistance(PointF)` | 4,096 / 4,096 |
| `GetDirection(PointF)` | 4,096 / 4,096 |
| `Vx` | 2,717 / 4,096 |
| `Vy` | 2,699 / 4,096 |

例如同一组坐标的距离，原版返回 `362.6282043457031`，未转换的现代实现返回 `362.62821008575713`。下列差异表描述基准对原始现代路径的定位；生产回归要求转换后与旧基准一致。

原版 `GetDistance` 的实际 JIT 代码已读回：`flds/fsubs` 做坐标差，`fmulp/faddp` 做平方和，`fstpl` 写入 double 临时量，随后 `fsqrt`。设备创建后的 24 位精度会作用于这些算术指令，最终返回的 double 不会恢复已经舍掉的精度。设备创建前的 53 位状态又会保留比当前 float 表达式更多的中间信息。

实际读回的核心指令对照（省略对象字段寻址；不是由反编译 C# 猜测出的指令）：

```asm
; CLR2 x86：原版 BaseObject.GetDistance(PointF)
flds   [positionX]
fsubs  [targetX]
; 同样的差值再载入，随后相乘；Y 分量同理
fmulp
faddp
fstpl  [doubleTemporary]
fldl   [doubleTemporary]
fsqrt

; .NET 8 Linux x64：当前 GetDistance(PointF)，FullOpts
vsubss xmm0, xmm2, xmm0
vmulss xmm0, xmm0, xmm2
vsubss xmm1, xmm2, xmm1
vmulss xmm1, xmm1, xmm2
vaddss xmm0, xmm1, xmm0
vcvtss2sd xmm0, xmm0, xmm0
vsqrtsd xmm0, xmm0, xmm0
```

`Vx/Vy` 的差异还包含 `Direction` setter 中反复减去 `2π` 的舍入，不能只替换三角函数就宣称该方法兼容。

`BaseObject.cs:871` 的位置更新公式在设备创建后的轨迹样本中也出现差异；同一条轨迹的首次差异可以早于 100 帧。这里是隔离公式轨迹，不是完整游戏 Replay。

### CLR 数学函数：实际指令不同

从原版运行时 `MethodHandle.GetFunctionPointer()` 指向的机器码读回并反汇编，确认：

| 原版方法 | 核心指令 |
|---|---|
| `Math.Sin` | `fldl; fsin` |
| `Math.Cos` | `fldl; fcos` |
| `Math.Atan2` | 有无穷输入特判；普通有限输入执行 `fpatan` |
| `Math.Sqrt` / 距离内联 | `fsqrt` |

同一批输入中，Sin、Cos、Atan2 分别有 34、28、14 项与当前结果不同。`fsin/fcos/fpatan` 与普通加减乘除受精度控制的方式不同；本次数据中它们的独立返回结果在 D3D 前后保持一致，sqrt 则在 D3D 后有 4,086 项不同。

接近零的结果会呈现很大的 ULP 距离但很小的绝对误差，不能仅据 ULP 数量判断视觉影响。是否改变碰撞分支仍需逐帧游戏验证。

### SlimDX：归一化语义不同

原版 `Vector2.Length` IL（RVA `0x65548`）明确将分量转成 double，平方、相加、调用 `Math.Sqrt`，最后转成 float。原版 `Normalize`（RVA `0x6559c`）在得到 float 长度后先计算并舍入 float 倒数，再乘分量；零值判断为 `!= 0`。

当前 `src/Platform/Compat/MathTypes.cs:22–35` 使用 `MathF.Sqrt(float 平方和)` 和直接除以长度。它既改变了中间精度，也改变了“倒数先舍入再乘”的算法。创建 D3D 后，本批长度样本恰好相同，两个归一化分量仍分别有 1,133、1,246 项不同；创建 D3D 前的长度另有 695 项不同。

`src/Shooting/AutoPlane.cs` 实际调用 Length/Normalize，因此这不是只有未调用兼容 API 才会遇到的差异。

### 纯渲染对照：复合旋转顺序不同（不修复）

原版 Matrix IL（RVA `0x70238/0x7026c`）调用 `Quaternion.RotationYawPitchRoll` 再调用 `Matrix.RotationQuaternion`。行向量下的几何顺序对应 `Rz(roll) * Rx(pitch) * Ry(yaw)`。

当前 `src/Platform/Compat/Matrix.cs:81–84` 为 `Ry * Rx * Rz`。这是几何语义差异，不是单纯浮点末位差异。三个被采样分量在 D3D 后分别有 4,093、4,096、4,096 项不同。还原几何顺序后仍须保留原版四元数构造与中间 float 舍入，才能追求位级结果。

目前源码没有发现该复合旋转 API 的实际游戏调用点；不能把它直接归因为已经观察到的背景异常。现有逐轴旋转、投影和 GPU 渲染是其他验证范围。

### 随机数：整数状态相同不等于浮点接口相同

实际旧 CLR 的 `System.Random.Sample` 将整数样本乘以 double 常量 `1.0 / Int32.MaxValue`；范围接口再乘范围宽度。`src/Shooting/MyRandom.cs` 的 `NextDouble` 还用乘 100 的结果更新 `ranValue`，`NextPMDouble` 执行 `2 * NextDouble() - 1`。这些步骤受调用线程精度影响，即使底层整数减法生成器保持相同状态。

设备创建前，真实原版与当前实现的 80,000 项全部相同；创建后有 39,464 项不同：

| 接口 | 不同 / 10,000 项 |
|---|---:|
| `Next()`、`Next(100)`、`Next(0,360)`、采样的 `ranValue` | 本批均为 0 |
| `NextDouble()` | 10,000 |
| `NextPMDouble()` | 10,000 |
| `Next(Int32.MinValue, Int32.MaxValue)` | 9,764 |
| `Next(Int32.MaxValue)` | 9,700 |

第一轮 `NextDouble` 是原版 `0.558161199092865`、当前 `0.5581611914365372`；大范围整数是原版 `-1200195968`、当前 `-1200195959`。小范围样本相同不能推广为所有范围永远一致。

因此，单独把旧算法转写到 .NET 8、或确认 .NET 8 选择了旧 Random 兼容路径，只能证明算法/特定精度条件下的兼容，不能证明原版游戏创建 D3D 后的随机数浮点行为兼容。

## 已验证的解决方案与实施边界

`SolutionsProbe.cs` 和 `legacy_x87.c` 是**实验原型，不被生产项目引用**：

1. 根据原版 IL/机器码保存计算顺序、float 落盘和 double 临时量边界
2. 对普通算术显式模拟 24/53 位状态；对原版超越函数使用 x87 指令，对组合方向/移动按原版顺序在原生侧计算
3. 归一化恢复“float 长度 → float 倒数 → 乘分量”；复合旋转恢复四元数构造及矩阵转换；`LegacyRandomModel.cs` 按真实旧 CLR 的算术步骤保留样本缩放、范围缩放和 `MyRandom` 记录值的舍入

两种精度状态各验证 **473,728 项**（合计 **947,456 项**），均为 **0 位模式差异**，覆盖采样的三角函数、sqrt、实际游戏方法、单次移动公式、向量、三个复合旋转矩阵分量、320,000 个轨迹坐标和 80,000 项实际随机数接口结果。

生产接入仅处理会影响游戏的路径：

- **游戏向量兼容修正**：恢复实际调用的 SlimDX 长度与倒数乘法，并将历史输出作为回归依据；纯渲染四元数/矩阵不接入生产修复
- **跨平台确定性数学策略**：明确游戏线程/初始化阶段需要的旧精度状态，固定逐操作舍入、随机数缩放和超越函数实现；Intel/AMD 原生 x87 可以作为验证后端，ARM64 需要经过同一基准验证的软件实现。不能简单依赖宿主线程的浮点环境

不能将本原型的 Linux x86_64 原生库直接当成全平台生产方案，也不能全局将所有计算改成 double。原型只覆盖当前有限、常规指数范围的数据；不是 x87 全指令集/异常/非正规数模拟器，未覆盖所有 NaN、无穷、极值、溢出与下溢行为，也不承诺不同 CPU 微架构的 x87 超越函数位模式全部相同。

`Pow` 作为运行时诊断被采集，在游戏源码中没有发现调用；其历史 CRT 算法未纳入候选解法的对齐断言。全部历史 Replay、全部旧 JIT 寄存器分配、所有微架构、所有极端输入及未调用向量 API 不在普遍兼容承诺内；GPU 数学明确排除。已有生产逐帧、碰撞、脚本、文本和实际回放门禁见上方入口。

## 复现

Linux x86_64 上运行隔离候选解法验证：

```sh
sh tests/testcase/MathBaseline/verify.sh
```

需要 .NET 8 运行时/SDK、Python 3、GCC。成功必须同时出现两个精度状态的零差异结果和最终 `MATH_BASELINE_SOLUTIONS_PASS`；这不代表生产代码已经修复。

Windows 重新采集使用同目录 `capture-legacy.ps1`。必须有 NetFx3 的 x86 CLR2、原版 THSSS/SlimDX，以及可用的 x86 D3DX9 依赖；D3D 采集必须在交互式桌面会话执行。SSH 服务的 Session 0 没有可枚举适配器时应明确失败，不能跳过 D3D 后的样本。采集写入临时工作目录和显式输出目录，不安装运行库、不保存登录凭据、不访问 `game/` 玩家数据。

原始压缩输出只允许由真实旧环境重新采集；不能用当前实现的结果覆盖“黄金基准”。

## 文档依据

- [微软 D3DCREATE：未指定 FPU_PRESERVE 时使用单精度就近舍入](https://learn.microsoft.com/en-us/windows/win32/direct3d9/d3dcreate)
- [微软关于旧 x86 JIT/x87 与现代浮点行为的说明](https://devblogs.microsoft.com/dotnet/floating-point-parsing-and-formatting-improvements-in-net-core-3-0)

运行时定位的原始机器码、完整本地采集日志和反编译取证还保留在本机 `~/.cache/thsss-math-baseline-2026/`。版本化套件不包含微软运行库或 DirectX 安装包。
