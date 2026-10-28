# 补丁素材（内嵌进 PdxLauncher.Core.dll）

这三个文件由 `PdxLauncher.Core.csproj` 以 `EmbeddedResource` 打进 DLL（`LogicalName=PdxLauncher.Patch.*`），
运行时由 `PatchAssets` 读取；外部目录 `data\patch\assets\` 下的同名文件优先，用于不重编译就换素材。

## 文件

| 文件 | 字节数 | sha256 |
| --- | --- | --- |
| `steam_api.dll` | 737280 | `a34e7ef464373898fa659294eef80ab554417c8ae6fef860f03fb7f5ae6bb698` |
| `steam_api64.dll` | 834560 | `f428bfe07eaf1e6f6fa1f28b6a787f93794bc5f97e8609645571b8bdc10d5385` |
| `cream_api.ini` | 26608 | `d493a262cf377e030402e49939deb2e7857439f71818f9c06c5cad2ad6e3d733` |

- `steam_api.dll` / `steam_api64.dll`：CreamAPI 4.x 的 Steamworks 代理 DLL（x86 / x64），导出与 Valve 原版同名接口，
  转发给同目录的 `steam_api_o.dll` / `steam_api64_o.dll`，并在 `SteamApps` 层伪造 DLC 归属。
- `cream_api.ini`：模板。`[steam]` 段的 `appid` 由启动器按目标游戏改写；`[dlc]` 段是 11 款 P 社游戏的 DLC id 合集
  （CreamAPI 只按查询到的 id 匹配，多列无害）。`orgapi` / `orgapi64` 保持 `steam_api_o.dll` / `steam_api64_o.dll`。

## 来源与用途

来自用户自备的本地整合包 `G:\Work\(内有说明)正版用DLC整合（1.13）\正版联机&DLC破解补丁\`，
仅用于**个人离线研究**（自用启动器，不分发）。仓库不含任何游戏本体或 DLC 内容文件。

## 改素材时必须同步

1. 上表的字节数与 sha256；
2. `tests/PdxLauncher.Checks/DlcPatchChecks.cs` 里的 `ExpectedLoaderX64Sha256` / `ExpectedLoaderX86Sha256` /
   `ExpectedIniSha256` 与长度常量；
3. 不要顺手把整合包里的 `steam_api64_o.dll` 一起放进来——它是**某款游戏的旧版原版 DLL**，
   跨游戏复用会让「移除补丁」还原出错误的版本。原版备份必须取自目标游戏自己的文件。
