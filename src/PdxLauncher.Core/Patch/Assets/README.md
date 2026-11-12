# 补丁素材（内嵌进 PdxLauncher.Core.dll）

这五个文件由 `PdxLauncher.Core.csproj` 以 `EmbeddedResource` 打进 DLL（`LogicalName=PdxLauncher.Patch.*`），
运行时由 `PatchAssets` 读取；外部目录 `data\patch\assets\` 下的同名文件优先，用于不重编译就换素材。

## 文件

| 文件 | 字节数 | sha256 |
| --- | --- | --- |
| `steam_api.dll` | 737280 | `a34e7ef464373898fa659294eef80ab554417c8ae6fef860f03fb7f5ae6bb698` |
| `steam_api64.dll` | 834560 | `f428bfe07eaf1e6f6fa1f28b6a787f93794bc5f97e8609645571b8bdc10d5385` |
| `cream_api.ini` | 803 | `ba1d9d4b95ed3eedb1276dbe38f170046e131ddefc875985b69d87006a67e03d` |
| `dlc-catalog.json` | 41824 | `34580dfb800810306180cf7dd51598a8179bfc05db6084963fc5519f2095c94c` |
| `known-versions.json` | 2319 | `03ac06486918db808b501d05faca1be379778ce60d83ff12dc135e229fe8e817` |

- `steam_api.dll` / `steam_api64.dll`：CreamAPI 4.x 的 Steamworks 代理 DLL（x86 / x64），导出与 Valve 原版同名接口，
  转发给同目录的 `steam_api_o.dll` / `steam_api64_o.dll`，并在 `SteamApps` 层伪造 DLC 归属。
- `cream_api.ini`：**模板**，只剩 `[steam]` / `[steam_misc]` / `[dlc]` 三个段头。`[steam]` 段的 `appid` 由启动器按目标游戏改写；
  `[dlc]` 段在打补丁时由 `dlc-catalog.json` 重新生成（见下），模板本身不再自带 490 条大表。
  `orgapi` / `orgapi64` 保持 `steam_api_o.dll` / `steam_api64_o.dll`。
- `dlc-catalog.json`：DLC **源清单**，按游戏分节（每节 `gameId` / `name` / `comment` / `steamAppId` / 可选 `sourceUrl` / `dlc[]`）。
  运行时 `PatchDlcCatalog.DlcLines()` 把它摊平成 `; <游戏注释>` + `id = name` 行，写进唯一一段 `[dlc]`。
  这样新增一行只改自己那一节，不会像 490 行大表那样错位；生成结果与旧大表逐字节一致（见 `DlcPatchChecks` 的
  `ExpectedVictoria3IniSha256`）。`validate()` 会拒绝：同一 id 出现在两个游戏名下、id 不是十进制正整数、id 重复、节缺 appId。
  新增条目时把商店页 URL 写进 `sourceUrl`（例如 Victoria 3 的 *State and Revolution*：
  `4314660` ← <https://store.steampowered.com/app/4314660/>，2026-11-05 随 1.15 上线）。
  2026-11-08 按 1.15 整合包补了 7 条（CS2 的 `4544310` / `4544320` / `4544340` / `4544350` / `4544360` / `4544370`
  与 EU5 的 `3698990`），总数 490 → 497。注意摊平后的 `[dlc]` 表包含**所有**游戏的条目，所以哪怕只加一节的条目，
  `ExpectedVictoria3IniSha256` / `ExpectedVictoria3IniLength` 也会变（本次 26,677 → 27,059 字节）。
- `known-versions.json`：**已知原版登记表**。每条记录 `gameId` / `version` / `buildId` / `fileName` / `sha256` / `size`，
  打补丁前用它判断目标 `steam_api*.dll` 是不是官方原版：命中 → 正常打；未命中 → Core 返回
  `GamePatchStatus.UnverifiedVersion` 且**不写盘**，由界面提示用户确认后再带 `allowUnverifiedVersion: true` 重试。
  只登记本机能采集到的真实条目（版本号取自各游戏 `launcher-settings.json`，buildid 取自 `appmanifest_*.acf`）；
  采不到的不猜（例如 EU5 的安装目录没有 `launcher-settings.json`，版本号无法采集，故未登记）。

## 来源与用途

来自用户自备的本地整合包 `G:\Work\(内有说明)正版用DLC整合（1.13）\正版联机&DLC破解补丁\`，
仅用于**个人离线研究**（自用启动器，不分发）。仓库不含任何游戏本体或 DLC 内容文件。

## 改素材时必须同步

1. 上表的字节数与 sha256；
2. `tests/PdxLauncher.Checks/DlcPatchChecks.cs` 里的 `ExpectedLoader64Sha256` / `ExpectedLoader86Sha256` /
   `ExpectedIniTemplateSha256` / `ExpectedVictoria3IniSha256` / `ExpectedVictoria3IniLength`、
   `ExpectedDlcTotal` / `ExpectedVictoria3DlcCount` / `ExpectedDlcSources` /
   `ExpectedCitiesSkylines2DlcCount` / `ExpectedEuropaUniversalis5DlcCount` 常量；
3. `tools/manual-build.py` 的 `core_resources()`（新增内嵌资源必须在这里补一行 `-resource` 映射，否则手工构建出来的 DLL 缺资源）；
4. 不要顺手把整合包里的 `steam_api64_o.dll` 一起放进来——它是**某款游戏的旧版原版 DLL**，
   跨游戏复用会让「移除补丁」还原出错误的版本。原版备份必须取自目标游戏自己的文件。
