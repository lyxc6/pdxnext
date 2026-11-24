# PDX.SDK（Paradox 官方 Mods / 账号客户端）

本目录是**随仓提交的二进制**，供 PdxNext 直接调用 Paradox 官方 SDK，实现 PDX Mods 的
登录态订阅、下载、PGP 验签与播放集缓存。它不是 Steamworks SDK（那个仍在 `sdk/` 下被
`.gitignore` 挡住，由用户自行下载）。

## 文件

| 文件 | 字节 | SHA-256 |
| --- | ---: | --- |
| `PDX.SDK.dll` | 1087488 | `3EDCDD4A11A1A36A36C03C26C8C70AEBF8BCC7491240AFE63A0490C2FE2C8724` |
| `PDX.SDK.Contracts.dll` | 117760 | `BE64307B066CBA449BE1CBD7ABED269E55D21778FE644CE2D536E51D9EF17FCC` |
| `PDX.SDK.Network.dll` | 30208 | `69DC55B8BB9D6996C162AE9890DABD76669C052A245AF1826EEE012536057749` |
| `External.dll` | 75264 | `717AB6ADE1581732C7F872CB787E4ED2A5463B61F868E9E2D60860B84B2A527A` |

## 来源

- 取自本机已安装的 **Skyve - Cities: Skylines II**（`D:\app\Skyve CS-II\`），文件时间
  2025-05-11 20:10:58，Skyve 自己也直接引用这套 SDK。
- 目标框架：`net48`（`PDX.SDK` 程序集）；PdxNext 主程序为 `net11.0-windows`。
  已在 `net11.0` 探针工程中验证可**直接** `Reference` 并成功执行
  `Context.Create(Platform.Windows, "cities_skylines_2", new Config())`。
- `PDX.SDK.dll` 依赖同目录的 `PDX.SDK.Contracts`、`PDX.SDK.Network`、`External`，
  没有其它 NuGet 依赖。

## 授权与用途

- `PDX.SDK` 是 Paradox Interactive 的闭源发布组件（PGP 根密钥注释为
  `Paradox Interactive Distribution (Root Key) <devops@paradoxplaza.com>`），
  **没有公开文档与授权条款**。本仓随附二进制只用于本机/本项目的功能实现，
  分发前请自行评估合规性。
- 反编译源码见 [`docs/PDX-SDK/`](../../docs/PDX-SDK/README.md)，只作阅读参考，
  **不参与编译**；运行时只引用本目录的 DLL，不把 SDK 内部类型暴露进 PdxNext 的程序集。

## 版本升级

替换 DLL 时同步更新上表哈希，并重新生成 `docs/PDX-SDK/`（命令见该目录 README）。
SDK 若引入不兼容 API，`PdxSdkHost` 的抽象边界允许退回到独立宿主进程方案。
