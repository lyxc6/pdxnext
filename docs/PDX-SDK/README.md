# PDX.SDK 反编译参考文档

本目录是 Paradox 官方 `PDX.SDK` 的**反编译源码**，只作阅读参考，**不参与编译**。
运行时使用的是 `sdk/pdx/*.dll` 四个二进制（见 [`sdk/pdx/README.md`](../../sdk/pdx/README.md)）。

## 生成命令

用本机已装的 `ilspycmd`（9.1.0.7988，ICSharpCode.Decompiler 9.1.0）生成：

```powershell
ilspycmd -p --nested-directories --disable-updatecheck -r sdk/pdx -o docs/PDX-SDK/PDX.SDK sdk/pdx/PDX.SDK.dll
ilspycmd -p --nested-directories --disable-updatecheck -r sdk/pdx -o docs/PDX-SDK/PDX.SDK.Contracts sdk/pdx/PDX.SDK.Contracts.dll
ilspycmd -p --nested-directories --disable-updatecheck -r sdk/pdx -o docs/PDX-SDK/PDX.SDK.Network sdk/pdx/PDX.SDK.Network.dll
```

替换 `sdk/pdx/` 里的 DLL 后必须重跑上述三条命令，保持文档与二进制一致。
生成后请删掉各子目录里的 `*.csproj`（反编译器会附带一个不可编译的工程文件），
避免有人误把参考源码当项目构建。

## 覆盖范围

| 目录 | `.cs` 文件 | 说明 |
| --- | ---: | --- |
| `PDX.SDK/` | 423 | `Context` / `Config` / 各服务实现 / `Platypatch` 下载器 / PGP 验签 / 播放集缓存 |
| `PDX.SDK.Contracts/` | 234 | 公开契约：`Config`、`Credential`、服务接口、结果模型、枚举 |
| `PDX.SDK.Network/` | 14 | HTTP / 传输层 |

重点看这几处（与 PdxNext 的订阅/下载直接相关）：

- `PDX.SDK/PDX/SDK/Context.cs` —— 初始化入口 `Context.Create(Platform, namespace, Config)`；
  会话恢复 `Account.Startup()` 读 `<DiskIORoot>\database.json` 的 `SessionToken`/`RefreshToken`。
- `PDX.SDK/PDX/SDK/Internal/Service/Mods/ModsSubscriptionService.cs` —— `Subscribe`：
  服务端 `MarkAsSubscribed` → `AddSubscribedModToPlayset` → `DownloadNewPdxMod` → `Enable` → 持久化。
- `PDX.SDK/PDX/SDK/Internal/Service/Mods/ModsDownloaderService.cs` —— 下载落盘与图片缓存。
- `PDX.SDK/PDX/SDK/Internal/Platypatch/` —— `patch/repo/<repositoryPath>/` 签名仓库、`manifest`、
  PGP 根密钥与逐文件校验。
- `PDX.SDK/PDX/SDK/Internal/Service/Mods/ModsCache.cs` —— `mods_subscribed` / `playsets_metadata` /
  `PlaysetSettings` 的本地布局，以及 `GetActivePlayset`。
- `PDX.SDK.Contracts/PDX/SDK/Contracts/Configuration/Config.cs` —— `DiskIORoot`、`Mods.RootPath`、
  `Ecosystem`、`DefaultHeaders` 等可配置项。

## 注意

- 反编译产物里的编译器生成类型（`<...>d__`、`--z__ReadOnlyList` 等）是正常的异步/集合实现细节。
- 反编译结果与原始源码可能有细微差异（变量名、`goto` 结构）；行为以 DLL 为准。
- 这些源码**不能**直接编译：它们依赖 SDK 自身的内部可见性，且引用了未公开的程序集。
