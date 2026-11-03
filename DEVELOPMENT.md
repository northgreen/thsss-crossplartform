## 构建与发布

M 系列 Mac 在首次准备好 .NET 8 或 10 SDK、Git LFS 资源和着色器工具后，从仓库根运行一个命令：

```sh
# 首次准备；已拉取 LFS 资源时不必重复
git lfs install
git lfs pull
brew install glslang spirv-cross

sh tools/package-macos.command

# 明确选择发布版本；两个包使用各自的目录
sh tools/package-macos.command --framework net8.0 --out dist/thsss-net8
sh tools/package-macos.command --framework net10.0 --out dist/thsss-net10
```

默认输出为 `dist/thsss-日期-时间/` 和同名 `.tar.gz`，包含 ARM64 游戏、Config、所选 .NET 运行时、资源和 Metal 着色器。
测试机无需另装 .NET、Homebrew 或着色器工具。此处是可携带的目录包
未拉取的 LFS 指针会让打包在构建前终止，并列出缺失资源示例。

其他平台的普通构建/发布入口：

```sh
dotnet build src/THSSS.csproj -f net8.0
dotnet build src/Config/THSSS.Config.csproj -f net8.0
dotnet publish src/THSSS.csproj -f net8.0 -c Release -o artifacts/publish
dotnet publish src/Config/THSSS.Config.csproj -f net8.0 -c Release -o artifacts/publish/ConfigApp
dotnet publish src/ConfigLauncher/THSSS.ConfigLauncher.csproj -f net8.0 -c Release -o artifacts/publish
```

发布版会附带只读资源和 `game/Setting.INI` 中的当前配置

完整分发使用 `perl tools/make-dist.pl --rid linux-x64 --framework net8.0`（按平台替换 RID）。
包根的 `Config`／`Config.exe` 是启动入口，实际配置界面及其依赖位于 `ConfigApp/`

### 数学兼容构建模式

编译参数 `THSSSMathMode` 支持三种模式，默认 `auto`：

| 模式 | 行为 |
|---|---|
| `auto` | 启用旧 CLR 数值兼容；x64 优先硬件 x87，ARM64 使用软件后端 |
| `software` | 启用相同兼容修复，强制软件 x87；冲突的 `THSSS_MATH_BACKEND=x87` 会明确报错 |
| `off` | 关闭兼容修复、数值 IL 织入及原生数学库，使用普通 .NET 数学；不保证旧 Replay 一致 |

```sh
dotnet build src/THSSS.csproj -c Release -f net8.0 -p:THSSSMathMode=software
dotnet publish src/THSSS.csproj -c Release -f net8.0 -p:THSSSMathMode=off -o artifacts/plain-dotnet
perl tools/make-dist.pl --rid linux-x64 --math-mode software
```

CI 手动运行 `Package` 时选择 `math_mode`；标签发布默认 `auto`。非默认模式的 CI 产物带 `-software` 或 `-off` 后缀。
包内 `THSSS.math-mode.json` 记录实际编译模式及游戏 DLL 哈希，不能通过重命名产物切换模式。
切换模式时正常执行 restore/build，不要复用另一模式的 `--no-restore` 或 `--no-build` 产物。

`auto` 构建仍可用 `THSSS_MATH_BACKEND=software` 在启动时强制软件后端，或用 `x87` 强制硬件；不支持的硬件后端会报错。
运行 `tools/run-game.sh` 会按默认模式重新构建，测试自选模式产物时直接运行该产物。

## 验证

```sh
sh tools/run-shader-regressions.sh
sh tools/run-behavior-regressions.sh
```

无图形桌面时可用 `ALSOFT_DRIVERS=null xvfb-run -a sh ...`。
