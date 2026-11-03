# 字体与图像（2026）

## 字体选择

游戏使用 SkiaSharp 4.154.0 与 HarfBuzzSharp 14.2.1.400 生成文字和位图。
OpenGL/Vulkan/Metal 负责游戏绘制。

`Setting.INI` 的 `[Game] FontType` 传入系统字体管理器。 Linux 原生包链接 `libfontconfig.so.1`，
别名、用户配置及自定义目录由 fontconfig 处理。 不能替换为 Linux.NoDependencies 原生包。
Windows/macOS 使用各自的平台字体管理器。 系统需安装所需字库，不自动下载或全局安装字体。

缺字按请求样式、UI 语言与 Unicode 字符再次匹配。 组合字符和 ZWJ 序列保留在同一文本元素，
尽可能选择覆盖整个元素的字面。 HarfBuzz 对连续同字体文本排版。 完全缺少字符时不能生成字形； 
不同机器的回退字形不保证相同。

字体家族与字面在进程内缓存。 修改系统字体配置后重启游戏。 配置窗口预览由 Avalonia 绘制，
其像素不等于游戏的最终文字像素。

## 测量与绘制

测量和绘制共用 `SkiaTextLayout`， 处理负左侧 bearing、字形边界、换行与回退度量。
point 按 96 DPI 转像素， 原生 D3D 风格高度构造器仍使用像素。
WordBreak、SingleLine、对齐和剪裁保留各自语义。

SlimDX 字体对象只缓存最近一份文字纹理。 文本、颜色、区域或标志变化会重绘纹理，
纹理更新和释放前提交已有 Sprite 批次。

## 图像与像素

`RasterCodec` 负责文件/流解码、转换、缩放和编码。

- GPU 上传为 top-down、straight-alpha RGBA
- Bitmap.Pixels 为同方向的 straight-alpha BGRA
- Skia 画布按 premultiplied alpha 混合

上传 GPU 或写回 Bitmap 前显式转换预乘格式。 不能将预乘颜色按直通 Alpha 上传。

PNG/JPEG 使用 Skia 编码器。 BMP 使用带 Alpha mask 的 V4 头；
TGA 为 32 位顶向下真彩；PPM 为二进制 P6。 默认 framebuffer 读回进行垂直翻转。
游戏 P 截图为 JPEG，诊断截图为 PNG。 读回绑定恢复与编码器相互独立。

调用方拥有传入的流，解码成功或失败都不关闭。 损坏、不支持的输入或原生库缺失明确失败，
不退回空白图片。
高位深转 8 位及抗锯齿不保证不同引擎逐像素相同。

## 配置程序与分发

Avalonia 11.3.22 配置程序使用 Skia 2.88.9 与 HarfBuzz 8.3.1.1，不能与游戏原生库混放。
不可只把配置程序的 Skia 单独升级成游戏版本。
分发结构如下：

```text
THSSS / THSSS.exe    游戏及其原生依赖
Config / Config.exe BCL 启动器
ConfigApp/          Avalonia 界面和独立依赖
licenses/SkiaSharp/ 许可原文与第三方告知
```

根 Config 传递原参数、数据根与退出码。 未指定数据根时使用包根 Setting.INI，
不在 ConfigApp 中创建第二套配置。 开发配置入口为 `tools/run-config.sh`。

许可原文及 SHA-256 清单在 `third_party/SkiaSharp/`。 原文按字节保留，不格式化或归一化换行。
打包检查版本隔离和许可完整性，原生库需匹配 RID。
交叉编译及架构检查不替代各平台运行。

## 验证

从仓库根执行：

```sh
(
  cd tests/testcase/SkiaCompatibility
  dotnet run -c Release -f net8.0
)
(
  cd tests/testcase/MathModePackaging
  python3 -B test_packaging.py
  python3 -B test_launcher.py
)
ALSOFT_DRIVERS=null xvfb-run -a \
  sh tools/run-behavior-regressions.sh
ALSOFT_DRIVERS=null xvfb-run -a \
  sh tools/run-shader-regressions.sh
```

图像检查覆盖文件头、PNG 扫描线、半透明色值、 padded stride、流所有权、错误传播及上传失败释放。

字体子进程使用私有 fontconfig 配置和字体目录， 与 fc-match 文件、集合索引和字体表比较，
不修改用户字体配置。 字符回退与特定别名按各自匹配结果核对，
不假定 CJK 集合中的各字面必须使用相同索引。

文字检查覆盖连字、组合字符、负 bearing、 混合脚本与 ZWJ 元素。
纹理上传和截图方向通过实际 GPU 读回验收。
