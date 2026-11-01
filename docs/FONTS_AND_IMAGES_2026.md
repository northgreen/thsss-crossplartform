# 字体与图像（2026）

## 字体选择与文字

游戏兼容层使用 SkiaSharp 4.154.0 与 HarfBuzzSharp 14.2.1.400；没有 SixLabors 依赖、许可校验任务或密钥要求。这里替换的是文字和图像位图的生产，OpenGL/Vulkan/Metal 仍负责游戏绘制。

`Setting.INI` 的 `[Game] FontType` 传入系统字体管理器。Linux 的常规 Skia 原生包链接 `libfontconfig.so.1`，字体别名、用户配置和自定义目录由 fontconfig 处理；不能使用 `NativeAssets.Linux.NoDependencies` 代替。Windows/macOS 使用各自的平台字体管理器。系统必须安装所需字库；配置窗口预览由 Avalonia 绘制，不能将预览像素等同于游戏最终字形。

缺字根据请求样式、当前 UI 语言和 Unicode 字符再次匹配。组合字符与 ZWJ 序列保持在同一文本元素中，尽可能选择覆盖整个元素的字面，HarfBuzz 对连续同字体的文本排版。字库完全不含某字符时无法制造其字形；fallback 也不保证不同机器显示完全相同。字体家族及字面由进程缓存，更新系统字体配置后重启游戏。

测量和绘制共用 `SkiaTextLayout`，包括负左侧 bearing、实际字形边界、换行及回退字体度量。点数按 96 DPI 转像素；原生 D3D 风格构造器的高度仍为像素。`WordBreak`、`SingleLine`、对齐和剪裁保留各自语义。SlimDX 字体对象只缓存最近一份文字纹理，文本、颜色、区域和标志改变会重新绘制，不重排已有 Sprite 提交。

## 图像与像素

`RasterCodec` 统一文件/流解码、像素转换、缩放与截图编码：

- 游戏纹理上传使用 top-down、straight-alpha RGBA；`System.Drawing.Bitmap.Pixels` 使用同方向的 straight-alpha BGRA。
- Skia 画布以 premultiplied alpha 混合，上传 GPU 或写回 Bitmap 前显式转换；不能把预乘颜色直接按直通 alpha 上传。
- PNG/JPEG 使用 Skia 编码器。BMP 使用带 alpha mask 的 V4 头，TGA 为 32 位顶向下真彩，PPM 为二进制 P6；这三个截图编码入口不因迁移而删除。
- 默认帧缓冲读回仍进行原来的垂直翻转；游戏截图仍是 JPEG，诊断截图为 PNG。读回目标和绑定恢复与图像编码器独立。
- 调用方传入的流由调用方持有，解码成功和失败都不关闭它。损坏或不支持的输入明确失败；原生库不可用不会退回空白图片。

不同图像解码器对高位深转 8 位和不同文字引擎的抗锯齿不保证逐像素相同；不以替换测试期望值掩盖颜色通道、alpha、布局或方向错误。

## 配置程序与分发

Avalonia 11.3.22 的配置程序使用 Skia 2.88.9 / HarfBuzz 8.3.1.1，与游戏的新原生库不能混放。维护更新不能单独将配置程序的 Skia 换成游戏版本。打包结构为：

```text
THSSS / THSSS.exe       游戏及其 Skia/HarfBuzz 库
Config / Config.exe     只依赖 BCL 的启动入口
ConfigApp/              Avalonia 配置界面及其独立依赖
licenses/SkiaSharp/     两套版本的上游许可和第三方告知
```

根 Config 启动器传递原参数、数据根与退出码；没有显式数据根时使用包根的 Setting.INI，不会改成 ConfigApp 内的第二份配置。开发态继续使用 `tools/run-config.sh`。

SkiaSharp/HarfBuzzSharp 许可原文及 SHA-256 清单在 `third_party/SkiaSharp/`。打包工具检查版本隔离和许可完整性；发布库必须来自对应 RID。六 RID 的编译和文件架构检查不等于六个平台都已实机运行。

## 验证

```sh
dotnet run --project tests/testcase/SkiaCompatibility/SkiaCompatibility.csproj -c Release -f net8.0
python3 -B tests/testcase/MathModePackaging/test_packaging.py
python3 -B tests/testcase/MathModePackaging/test_launcher.py
ALSOFT_DRIVERS=null xvfb-run -a sh tools/run-behavior-regressions.sh
ALSOFT_DRIVERS=null xvfb-run -a sh tools/run-shader-regressions.sh
```

独立 PNG 扫描线、各截图格式字节头、半透明色值、padded stride、流所有权、原生错误和上传失败释放均有断言。字体测试在子进程使用私有 fontconfig 配置和字体目录，与 `fc-match` 的文件/集合索引及实际字体表比较，不更改用户配置。同一 CJK 集合的多个字面可能都声明中文覆盖；字符回退与特定别名分别对照各自的原生匹配结果，不假定它们必须选中同一索引。文字还验证连字、组合字符、负 bearing、混合脚本和不拆分 ZWJ 元素；文字纹理与截图方向由实际 GPU 读回验收。
