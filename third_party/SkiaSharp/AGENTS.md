# SkiaSharp 与 HarfBuzz 分发许可

`license-inventory.json` 记录实际 NuGet 包的许可文件原始 SHA-256。保留全部上游文字，不翻译或裁剪；更新包时同步对应许可与 inventory。

游戏使用 SkiaSharp 4.154.0 / HarfBuzzSharp 14.2.1.400；配置程序的 Avalonia 11 使用 SkiaSharp 2.88.9 / HarfBuzzSharp 8.3.1.1。发布必须隔离两套原生及托管依赖，不能把 ConfigApp 的文件复制覆盖游戏根目录。此目录随发布包放入 `licenses/SkiaSharp/`，不需要许可证密钥。
