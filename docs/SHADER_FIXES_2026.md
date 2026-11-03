# Shader 与光照（2026）

## FX 效果

Effect 兼容层加载游戏提供的 Blur/Filter/Warp 源， 保存已声明参数，管理单 technique/pass 生命周期，
并由实际 Sprite 绘制调用对应程序。

FX 是已提供算法的保真移植，不是通用 HLSL 编译器。 缺失文件、不支持源码、未知参数、错误类型、
非法 pass 顺序和释放后调用均明确失败。 加载失败释放已创建资源，不循环重试。

Sprite 输入方向与源纹理方向独立处理： `uInputFlip` 表示 Sprite 坐标方向，
`uSourceFlip` 表示实际纹理方向。 FX 在逻辑 UV 中计算，再映射到采样方向。
Bind/Unbind 保存并恢复外部 sampler。

## 混合、视口与深度

Flush 保留当前混合设置；Begin/End 保存恢复批次状态。 支持 Add、Subtract、ReverseSubtract、Min、Max。
窗口与离屏视口均使用调用者 X/Y/W/H、MinZ/MaxZ。 窗口逻辑视口经过 letterbox 映射。

离屏目标可挂接真实 D16 深度附件。 附件按目标尺寸匹配，外部深度为借用资源。
默认 framebuffer 不修改附件，保留窗口深度。 清深度临时开启 DepthMask，完成后恢复。

Surface 构造和切换分别保存 read/draw FBO。 只释放自己拥有的纹理与深度资源。
ScreenTexture 不释放借来的目标，Reset 替换自身资源。

## 模型、顶点与采样

文本 `.x` 解析支持 Frame 变换、材质、UV、 独立 Normal 索引、subset 与凹多边形三角化。
Mesh 使用真实 VBO 绘制，支持 Clone 和法线计算。 binary/compressed `.x` 明确报不支持。

顶点声明的 offset 与实际 stride 独立。 支持 Position、UV、Normal、packed D3DCOLOR，
以及带尾部 padding 的布局。 FVF 与声明切换时清理旧布局，两组顶点颜色独立。

3D 默认 Wrap；Sprite 默认 Clamp 与线性过滤。 GL 采样支持 Wrap/Clamp/Mirror/Border、
Point/Linear 与 mip 过滤。 纹理最大层级与实际层数一致。

GL 窗口按 AntiAlias 配置请求 4 samples， 同时申请 depth24/stencil8。
实际可用采样数由平台驱动决定； 不能将 GL 能力视为所有后端的共同能力。

## 固定管线光照

法线先经过 world/view 逆转置。 `NormalizeNormals` 默认 false；开启时才归一化。
零法线保持零，避免产生 NaN。

`ColorVertex` 默认 true，各分量来源如下：

- Diffuse 默认 Color1
- Specular 默认 Color2
- Ambient 与 Emissive 默认 Material

关闭 ColorVertex 时全部使用对应材质。 缺少指定顶点颜色时也回退对应材质。
支持 Color usageIndex=1 与 FVF Specular。 packed BGRA 与 Float4 按各自布局读取。
Diffuse 来源的 Alpha 决定光照结果 Alpha。

方向光 Diffuse、材质 Ambient/Emissive 与高光 按固定管线语义组合，高光在纹理调制之后相加。
LocalViewer 默认使用顶点到相机视线。 活动纹理 stage 使用 Texture×Diffuse Modulate。

相关状态编号：

- SpecularEnable：29；LocalViewer：142
- NormalizeNormals：143
- AmbientMaterialSource：147
- EmissiveMaterialSource：148

## 资源与支持范围

发布包含 FX、Model、Image、BGM、Sound、Story、 CS、BPic 与 Setting.INI。
首次启动创建 Replay、ScreenShot 等运行输出目录。 独立发布使用程序集目录作为默认数据根。
开发运行通过 `tools/run-game.sh` 使用 `game/`。

DrawUserPrimitives 上传实际临时 VBO。 通用 DrawIndexedPrimitives 明确报不支持；
Mesh 展开合法索引后使用 DrawPrimitives。

当前光照覆盖游戏使用的方向光、环境光、材质与法线。 任意 D3D 多纹理组合及全部光照类型不在支持范围。

## 验证

从仓库根运行：

```sh
ALSOFT_DRIVERS=null xvfb-run -a \
  sh tools/run-shader-regressions.sh
```

SDK 配置以根 `global.json` 为准。 回归使用严格断言和非零失败退出码，覆盖以下内容：

- FX 对照原公式的 CPU/GPU RGBA 数值
- 非对称上传、离屏、Sprite 与 FX 方向
- sampler 恢复、混合、深度及局部视口
- 模型、顶点布局、光照来源及法线变换
- uniform 更新后的实际 GPU 数据与像素

数值参考和固定管线预期不等于原版逐帧截图金标准。 完整关卡、Boss 画面及各驱动行为需独立运行验证。
