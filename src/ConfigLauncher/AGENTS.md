# 配置启动入口

此独立 BCL-only 项目输出包根的 `Config`／`Config.exe`，不可引用 Avalonia、Skia 或游戏程序集。真正配置界面及原生依赖位于 `ConfigApp/`，防止与游戏的另一版 Skia/HarfBuzz 相互覆盖。

启动前将继承的相对数据根按原工作目录转成绝对路径，统一传给子进程环境及工作目录；默认数据根为包根。使用 `ArgumentList` 传递参数并返回子进程退出码，缺失子程序必须明确失败，不创建玩家数据目录。回归 `tests/testcase/MathModePackaging/test_launcher.py` 包括外置数据的实际“应用并启动”路径。
