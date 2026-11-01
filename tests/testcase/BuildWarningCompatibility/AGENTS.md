# 构建警告修复的行为边界

运行 `dotnet run --project tests/testcase/BuildWarningCompatibility/BuildWarningCompatibility.csproj -c Release -f net8.0`，已纳入根行为回归。

- 检查31个设计器占位事件的延迟存储、重复订阅、最后匹配项退订及继承隐藏事件隔离，不为消警告新增运行时触发。
- 真实键盘、鼠标、Resize/Paint分派的字段事件保留；测试断言载荷及顺序。
- 组件类型必须来自BCL，游戏程序集不能再声明 `System.ComponentModel` 替身。
- 未使用的 `Shooting.Properties.Resources.resources` 不嵌入；主窗体资源清单独立保留。当前主窗体resx无数据，Icon为空是现有行为。

测试不访问玩家数据、不启动真实窗口。原 `RenderForm_Main.Dispose(bool)` 的递归调用不在本测试修复范围，构造资源检查不调用该释放路径。
