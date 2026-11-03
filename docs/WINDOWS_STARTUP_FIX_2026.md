# Windows 启动与资源（2026）

## 构建和运行

从仓库根运行 `sh tools/run-game.sh`。 Windows 自包含发布显式指定 RID：

```sh
dotnet publish src/THSSS.csproj \
  -f net8.0 -c Release -r win-x64 \
  --self-contained true
```

发布游戏资源以项目构建配置为准。 独立配置程序及启动器需分别发布；
完整分发使用 `tools/make-dist.pl`。

```sh
perl tools/make-dist.pl --rid win-x64 \
  --framework net8.0 --self-contained
```

## 图集键与文件枚举

`GlobalDataPackage.TextureObjectLoader` 通过字典索引赋值注册图集键和整图键。
同名定义采用后加载覆盖先加载的语义。

不同系统的文件枚举集合及顺序可能不同。 不得假设 Directory.GetFiles 已排序，
也不能用 Dictionary.Add 处理允许覆盖的键。 资源键注册不修改原资源或图集定义。

文件枚举顺序也可能决定关卡控制器创建顺序。 Replay 测试应保留资产树的实际枚举顺序，
不能为消除测试差异而全局排序生产资源加载。

## 启动要求

字体由 Windows 字体管理器选择， 图像和文字依赖匹配 RID 的 Skia/HarfBuzz 原生库。
游戏依赖与 ConfigApp 依赖必须隔离。 细节见[字体与图像][fonts]。

[fonts]: FONTS_AND_IMAGES_2026.md

音频默认系统 XAudio2 2.9。 渲染默认 OpenGL，可显式选 Vulkan。
初始化错误须保留具体异常，不静默更换后端。

Windows 平台 DES 使用系统提供程序。 Wine/Proton 缺少 DES 时，可使用编译期回退，
见[托管 DES][des]。

[des]: MANAGED_DES_FALLBACK_2026.md

## 验证

```sh
sh tools/run-behavior-regressions.sh
sh tools/run-shader-regressions.sh
sh tools/run-resize-regressions.sh
sh tools/run-audio-regressions.sh
```

图形、输入与音频检查需可用的交互桌面和设备。 SSH 中进程存活不代表画面、听感或存档正确。
Wine 检查不替代原生 Windows 驱动检查。 测试使用临时数据根，保留玩家配置和存档。
