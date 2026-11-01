# Skia/fontconfig 回归

此独立程序已纳入行为回归。运行 `dotnet run --project tests/testcase/SkiaCompatibility/SkiaCompatibility.csproj -c Release -f net8.0`。

- PNG 输入由字面扫描线、zlib和CRC构造，不用被测编码器产生全部预期；BMP/TGA/PPM以独立字节读取器校验容器、像素和方向。
- `FontconfigChecks` 在子进程使用私有 fonts.conf 和复制的 CJK 集合，校验强别名、自定义目录、非零 TTC 索引及实际字体表；语言回退与独立 fc-match 同查询比较，不能拿另一个别名的结果作期望。Linux测试需要 fontconfig 工具及 Noto Sans CJK SC 的非零集合字面；不改用户字体配置。
- `ShapingChecks` 使用真实连字字体 DejaVu Serif，覆盖组合、负 bearing、混合脚本和 ZWJ 文本元素，度量必须包含实际栅格边界。
- `RasterBoundaryChecks` 覆盖流所有权、padding、预乘/直通 alpha、色域标签、无原生库显式错误及纹理上传异常释放。模拟后端只用于分配/释放，颜色与文字仍由真实 Skia 栅格化。

生产截图和文字缓存的实际 GPU 行为仍在 `TextScreenshotCompatibility` 与 shader 套件中断言，不能以本程序的 CPU 栅格化代替 GPU 验收。
