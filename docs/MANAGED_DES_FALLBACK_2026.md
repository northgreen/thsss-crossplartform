# 托管 DES 回退（2026）

## 适用环境

Windows 上 .NET DES 使用系统 CNG 提供程序。 Wine/Proton 环境可能不提供 DES，
导致存档加解密抛出 CryptographicException。 可为此类环境编译托管回退，不改变存档格式。

## 构建开关

默认构建不包含回退代码。 `THSSSManagedDesFallback=true` 启用编译常量
`THSSS_MANAGED_DES_FALLBACK`。

普通 Windows 发布：

```sh
dotnet publish src/THSSS.csproj \
  -f net8.0 -r win-x64 --self-contained
```

包含 Wine/Proton 回退的发布：

```sh
dotnet publish src/THSSS.csproj \
  -f net8.0 -r win-x64 --self-contained \
  -p:THSSSManagedDesFallback=true
```

运行时先尝试平台 DES， 仅在 CryptographicException 时使用已编译回退。
未启用回退则抛出明确错误并提示构建参数。

## 实现契约

`Encryption.Cipher()` 返回 SaveCipher：

- PlatformCipher 使用 `DES.Create()`
- ManagedCipher 使用托管 CBC 与 PKCS#7

ManagedDes 实现 FIPS 46-3 的 DES 置换、 16 轮 Feistel、S 盒和子密钥生成。
密钥、IV、CBC 与填充保持原游戏格式。 此入口用于格式兼容，不提供新的安全加密协议。

## 验证

```sh
sh tools/run-behavior-regressions.sh
```

ManagedDesCheck 对照标准向量和平台 DES， 检查空串、ASCII、中文、多行与双向解密。
标准单块向量如下：

```text
key       133457799BBCDFF1
plaintext 0123456789ABCDEF
cipher    85E813540F0AB405
```
