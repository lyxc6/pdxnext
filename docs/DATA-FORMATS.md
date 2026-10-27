# PDX Next —— 数据格式与真机事实（T0 冻结）

> 本文档中的所有数字/字节均由 Lead 在本机实测得到，是实现的唯一依据。
> 修改本文档只能由 Lead 执行。

## 0. 本机真机环境

Steam 三个库：D:\app\Steam（"0"）、E:\SteamLibrary（"1"）、F:\SteamLibrary（"2"）。

| gameId | 安装目录 | launcher-settings.json 位置 | version | formatVersion | dlcPath |
| --- | --- | --- | --- | --- | --- |
| victoria3 | D:\app\Steam\steamapps\common\Victoria 3 | <root>\launcher\launcher-settings.json | 1.13.11 (Matcha) | **1.1** | ../game |
| ck3 | E:\SteamLibrary\steamapps\common\Crusader Kings III | <root>\launcher\launcher-settings.json | 1.20.1.2 (Crozier) | **0** | ../game |
| stellaris | F:\SteamLibrary\steamapps\common\Stellaris | <root>\launcher-settings.json（**在安装根，无 launcher 目录**） | Cygnus v4.5.2 (9776) | **缺省→0** | 缺省 ./ |
| prison_architect_pc_console | F:\SteamLibrary\steamapps\common\Prison Architect | <root>\Launcher\launcher-settings.json（**大写 L，非 PDX schema**） | The_Jailhouse_1.02 | 1 | ./ |

> 本机游戏数量会漂移：用户在 Steam 上新装/首次运行游戏时，官方启动器会在游戏目录写
> `launcher-settings.json`，本启动器随即把它收进列表（实测 2026-10-27 从 5 款涨到 7 款，
> 多出 `eu4` / `cities_v2`，两者都按「未适配」展示）。所以自检只断言「已知这几款必须在」，
> 不锁死总数（`tests/PdxLauncher.Checks/GamesChecks.cs`）。

支持等级（`GameSupport`）：victoria3 / ck3 / stellaris = **完整适配**（Full）；
prison_architect_pc_console = **仅启动**（LaunchOnly，见第 11 节）；未登记的 gameId = **未适配**（Unsupported）。

EU5（appid 3450310）**未安装**：只有 Documents 残留（dlc_signature + continue_game.json，无 sqlite），安装目录已删。
⇒ 判定"已安装"必须看**安装目录里是否存在 launcher-settings.json**，不能看 Documents 目录。

发现来源（`GameCatalog`，无 gameId 白名单）：

1. Steam 库：`steamapps\common\*` 下找 launcher-settings.json（三个约定位置见第 1 节）。
2. 官方启动器独立库：`%APPDATA%\Paradox Interactive\launcher-v2\userSettings.json` 的 `gameLibraryPaths`：
   字符串条目 = 库根（相对路径按 `%LOCALAPPDATA%\Programs\Paradox Interactive\launcher` 解析，
   官方默认 `../games`）；对象条目取 `installationPath`；另加默认目录
   `%LOCALAPPDATA%\Programs\Paradox Interactive\games`。库根自身与一级子目录都检查。
   本机该默认目录不存在，所以本机仍然只有 Steam 那 4 个游戏。
3. 同一 gameId 多来源时保留先发现的（Steam 优先，SteamAppId 可读）；独立库安装的 SteamAppId 为 null。
   未登记 gameId 只影响支持等级（Unsupported）与展示名兜底，不影响是否收录。

游戏数据目录（gameDataPath 展开 %USER_DOCUMENTS%）：
- CK3：C:\Users\17147\Documents\Paradox Interactive\Crusader Kings III
- V3：C:\Users\17147\Documents\Paradox Interactive\Victoria 3
- Stellaris：C:\Users\17147\Documents\Paradox Interactive\Stellaris

## 1. launcher-settings.json

路径探测顺序（大小写不敏感）：<root>\launcher-settings.json → <root>\launcher\launcher-settings.json → <root>\Launcher\launcher-settings.json。

关键键（括号内为缺省值）：
- gameId（必填）、displayName（Stellaris 缺 → 回退 gameId 或元数据 name）
- version / rawVersion
- formatVersion（缺省 **0**；V3 是 1.1）
- gameDataPath（必填，含 %USER_DOCUMENTS% 需展开）
- dlcPath（缺省 "./"，相对安装根）
- modPath（缺省 "mod"）、modExtension（V3 "zip"）
- themeFile（如 "./assets/theme-settings.json"，相对安装根）
- exePath（相对安装根，如 "../binaries/ck3.exe" / "./stellaris.exe"）
- exeArgs（三款游戏都是 ["-gdpr-compliant"]；PA 是 ["--launched-from-launcher"]）
- alternativeExecutables：[{exePath, exeArgs, label{de,en,...,zh-hans,zh-hant}}]
- browserDlcUrl / browserModUrl（V3 为空字符串）
- ingameSettingsLayoutPath（settings-layout.json）、modStubberPath

## 2. 加载文件（启动器唯一必须写对的东西）

格式由 formatVersion 决定：

| formatVersion | 文件名 | 键 | 值的含义 |
| --- | --- | --- | --- |
| 0 | dlc_load.json | disabled_dlcs, enabled_mods | disabled_dlcs = **相对 dlcPath 的 POSIX .dlc 路径**；enabled_mods = **mods.gameRegistryId**（如 mod/ugc_2218867072.mod） |
| 1 | content_load.json | disabledDLC, enabledMods, enabledUGC | disabledDLC = [{paradoxAppId: pops_id}]；enabledMods = [{path: archivePath}]（无 archivePath 的 mod 丢弃）；enabledUGC = [{path: 绝对目录}] |
| 1.1 | content_load.json | 同上 | enabledMods = 先 [{path: dirPath}] 再 [{path: archivePath}]（**两组拼接，不是全局排序**） |

**序列化规则（必须逐字节遵守）**：
- JSON.stringify 语义：紧凑、无缩进、无换行、UTF-8 **无 BOM**。
- 中文**不转义**（等价于 C# 的 JavaScriptEncoder.UnsafeRelaxedJsonEscaping）。
- **不写 formatVersion 键**（schema 允许但官方从不写）。
- 键序：沿用已存在文件的键序（官方用 defaultsDeep({}, 现有内容, 默认值)）。真机 V3 是 enabledUGC, disabledDLC, enabledMods；CK3/Stellaris 是 disabled_dlcs, enabled_mods。

真机样本（tests/fixtures/，**字节级 golden**）：
- ck3/dlc_load.json 186 B，md5 d44d673a81c5d8bfb62d0a0bbd0364da：
  {"disabled_dlcs":[],"enabled_mods":["mod/Less Old Wives - AI Men Marry Younger Fertile Women.mod","mod/LYXC的更多游戏规则.mod","mod/猎艳寻欢.mod","mod/legends never die.mod"]}
- stellaris/dlc_load.json 374 B，md5 45494607b40e9dd60667fe29deb73f43（12 个 mod）
- victoria3/content_load.json 1646 B，md5 fdef3e859c807021eb6aa8c66d2720d8（22 个 {"path":"D:\\app\\Steam\\steamapps\\workshop\\content\\529340\\<id>"}）

**写入流程**：tmp 文件 + File.Move(overwrite:true) 原子替换；写前把旧文件备份成 <name>.bak；用 SemaphoreSlim 串行化。

**启动本身不重写加载文件**（官方行为）。UI 里改 playset 才写。

## 3. DLC

| 格式 | 扫描 | 元数据来源 |
| --- | --- | --- |
| V0（CK3/Stellaris） | <dlcPath>/**/*.dlc | Clausewitz k=v 文本 |
| V1 / V1_1（V3） | <dlcPath>/**/*.dlc.json | JSON |

真机数量：CK3 **25** 个 .dlc（0 个 .dlc.json）；V3 **17** 个 .dlc.json（也有 17 个 .dlc）；Stellaris **31** 个 .dlc（0 个 .dlc.json）。

.dlc 键集（三款不同，解析器必须容错）：
- CK3（tests/fixtures/ck3/dlc001.dlc，264 B）：name, path, steam_id, pops_id, msgr_id, affects_checksum, localizable_name, checksum
- V3（tests/fixtures/victoria3 无 .dlc 样本，用真机 241 B 那个）：name, path, steam_id, pops_id, mp_synced, affects_save_compatibility, localizable_name, checksum
- Stellaris（tests/fixtures/stellaris/dlc001.dlc，392 B）：name, localizable_name, archive, steam_id, msgr_id, rail_id, pops_id, affects_checksum, affects_compatability, zip_checksum, third_party_content, category, thumbnail

.dlc.json schema（tests/fixtures/victoria3/dlc001_preorder.dlc.json，314 B）：
{"id":{"steam":"2071470","paradox":"dlc001_preorder"},"displayName":{"en":"..."},"category":{"en":"DLC"},"description":{"en":"..."},"thumbnailPath":"thumbnail.png","thirdPartyContent":false}
⇒ id.paradox **必填**，displayName.en **必填**，thumbnailPath 相对该 .dlc.json 自身目录。

DLC 对象字段：FilePathRelativeToDlcPath = 相对 dlcPath 的 POSIX 路径（如 dlc001_preorder/dlc001.dlc）；ThumbnailPath = 同目录 thumbnail.png（280×160）；PopsId 来自 pops_id / id.paradox。

DLC 列表**不来自 sqlite**（CK3 的 dlc 表 0 行）——必须扫盘。

## 4. launcher-v2.sqlite

路径：<gameDataDir>\launcher-v2.sqlite。CK3 233 KB / V3 / Stellaris 都有；EU5 无。

访问方式：**P/Invoke C:\Windows\System32\winsqlite3.dll**（sqlite 3.51.1，导出 sqlite3_open_v2 / prepare_v2 / step / column_text / column_int / column_type / finalize / close / errmsg / column_name / bind_text / free / libversion）。**不要引 NuGet**（离线无包）。

表：mods、playsets、playsets_mods、playsets_dlcs、key_value_pairs、mods_dependencies、dlc、metadata_relationships、ugc、transient_ownership、knex_migrations。

读取真值：
- CK3：3 个 playset —— 23f455bd-37a2-48de-8ce8-f816b79fe541("1.18",0)、4ffda78b-5b4c-4e2c-ae08-50540585db24("1.20",0)、4ffbbec6-db91-4126-9086-15e6fb1e013e("IronyModManager",**isActive=1**)；激活 playset 5 行（position 0..4，启用 4 个）。
- V3：1 个 playset bc73b308-3180-48a6-9c0f-3c3b92153c25("1",active=1)，23 行全启用（2026-10 实测；官方启动器会继续追加）。
- Stellaris：5 个 playset，激活 853346c4-…("IronyModManager")，13 行全启用。

映射规则（官方行为）：
playsets WHERE isActive=1 → playsets_mods ORDER BY position ASC（enabled=1 才算）→ mods.gameRegistryId 即 enabled_mods；
playsets_dlcs 中 enabled 为假的行 → pops_id 即 disabledDLC。
**无激活 playset ⇒ disabled 列表为空（全部 DLC 启用）**。
CK3 的 playsets_dlcs 为空 ⇒ disabled_dlcs: []，与真机文件一致。

### Mod 库（mods 表全量，只读）

官方启动器左侧「MOD 库」的数量徽标 = `SELECT COUNT(*) FROM mods`（V3 实测 64），列表就是 mods 表全表。

- 读取列：id、displayName（`name` 常为 NULL，展示名回退链 displayName → name → dirPath 目录名）、dirPath、archivePath、gameRegistryId、thumbnailPath、requiredVersion、status、source、size、subscribedDate。**不要写死列清单**：不同游戏/版本的库缺列（例如旧库没有 name/size/subscribedDate），用 `SELECT *` 按列名取、缺列当 null。
- size 是**字节数**（INTEGER）；subscribedDate 是 **unix 秒**（个别写法是毫秒，>1e11 按毫秒解）。实测：size=1034853 / subscribedDate=1766506698 → 官方 UI 显示 "1010.6KB"、"2025/08/04"（本地时区）。
- 「在 N 个播放集中」= playsets_mods 中该 modId 出现的不同 playsetId 数（V3：23 行全部属于唯一的播放集 "1"）。
  不属于任何播放集时这段文案为空串（原来每行都打印「没有在播放集中」，387 行里绝大多数如此，纯噪音）。
- 支持版本 = `mods.requiredVersion`（CK3 实测形如 `1.19.*` / `1.20.*` / `1.*.*`），来源是 .mod 的
  `supported_version` 或 metadata.json 的 `supported_game_version`。界面原样显示为「支持版本 1.19.*」，
  不做换算、不补全通配符。
- 创意工坊 id（供「在 Steam 中打开」/「在浏览器中打开」）：优先 `mods.steamId`，为空时从 `gameRegistryId`
  解析 `mod/ugc_<id>.mod`（CK3 387 行里 382 行是这种形状）。旧库没有该列时按 null 处理，与「本地 mod 没有
  创意工坊页面」是同一种展示结果，不是错误。地址：`https://steamcommunity.com/sharedfiles/filedetails/?id=<id>`
  与 `steam://url/CommunityFilePage/<id>`。
- 磁盘扫描（ModCatalog）会合并 workshop 目录里尚未登记进 mods 表的 mod：这类行没有 size/subscribedDate、没有播放集归属，由 `ModLibraryEntry.IsUnregistered` 标记（source 仍是 local，但与数据库里正式登记的 source=local mod 不同——后者仍在官方库里、有大小、能加进播放集），**不能写入 playsets_mods**（外键指向 mods.id）。
- 列表行的警告三角（对齐官方语义）有三种来源：未登记的磁盘 mod、status ≠ ready_to_play、requiredVersion 的 major.minor 低于当前游戏版本（只比较 major.minor，例如 V3 的 [1.11] mod 对 1.13 游戏）。
- 读取一律只读打开；仓库层在托管侧按名称 OrdinalIgnoreCase 排序（SQLite 的 COLLATE NOCASE 只折叠 ASCII，和 .NET/中文排序不一致）。

## 5. mod

- .mod 描述符目录：<gameDataDir>\mod。CK3 **382** 个 .mod（2026-10-27 实测，此前 387）；Stellaris **313**；V3 **0**（V3 mod 全在 workshop）。
- .mod 键：name→displayName、version、tags、picture、supported_version→requiredVersion、path→dirPath、archive→archivePath、remote_file_id→steamId。
- 命名：ugc_<steamId>.mod / pdx_<pdxId>.mod / <dirname>.mod；本地目录 mod 目录内有 descriptor.mod。
- V3 workshop mod 无 .mod：元数据在 <modDir>\.metadata\metadata.json，键 name, id, version, supported_game_version, short_description, thumbnail, tags, relationships, game_custom_data。
- 缩略图解析（`ModThumbnailResolver`，Mod 库与播放集共用一套）：候选**目录**依次是 `dirPath` → `archivePath` 所在目录 → 把 `dirPath` 里 `steamapps\workshop\content\<appid>\<modId>` 这段尾巴重新拼到每个 Steam 库根 → `steamapps\workshop\content\<game.SteamAppId>\<steamId 或 gameRegistryId 里的 ugc_<id>>` → `InstallRoot\..\..` 当 steamapps 根再试一遍（Stellaris 的 `dirPath` 停在旧盘 E:\，mod 实际在 F:\，靠这一层重定位）。目录内文件名优先级：`.mod` / metadata 的 `picture`（= metadata 的 `thumbnail`）→ `thumbnail*` 里精确的 `thumbnail.png` → 其它 `thumbnail.<png|jpg|jpeg|bmp|gif|webp>`（`.pdn/.psd/.xcf` 不算图）→ 最后才用 sqlite 的 `thumbnailPath`（官方 `.launcher-cache`，须真实存在）。**不改写 `dirPath`**：加载文件 V1_1 与播放集 `dir:` 匹配键都依赖它。
- 联网补图（可选，`ModThumbnailService`）：本地解析不到时用 `mods.thumbnailUrl` 下载（域名
  `images.steamusercontent.com`，已加入 `OnlineHosts` 白名单），落 `<程序目录>\data\mod-cache\mod-thumbnails\<modId N>.<ext>`，
  索引 `mod-cache\mod-thumbnails.json` = `{version, entries: {<modId N>: {url, fileName, fetchedUtc, failedUtc}}}`。
  保留天数在设置页「联网图片」里配（7 / 30 / 90 / 365 天，默认 30，存 `AppSettings.ModThumbnailCacheDays`，越界钳到 1..3650）；
  打开 Mod 库时后台静默补（不阻塞列表、列表用 `OneWay` 绑定就地刷新），失败 6 小时退避、连续 2 次失败放弃本批；
  只补「本地无图且有 thumbnailUrl」的行，关闭开关后**零请求**，只用已落盘的缓存。
- mods_registry.json（CK3 146,847 B / Stellaris 116,349 B，V3 无）**是只读遗留**：官方新版只读它并在迁移后删除。

Workshop 目录：<steamapps>\workshop\content\<appid> —— 529340=42 项、1158310=384 项、281990=307 项。

## 6. 主题

安装目录素材：<root>\launcher\assets\（CK3/V3）、<root>\assets\（Stellaris）、<root>\Launcher-assets\（PA）。

theme-settings.json 结构：{appIcon:{appIconPath}, appTaskbarIcon:{appIconPath}, background:{backgroundImage}, logo:{imagePath}, button:{backgroundImage,color,fontSize,fontWeight,fontPath,textShadow}, buttonHover:{color,...}, modsPlaceholder:{imagePath}, newsPlaceholder:{imagePath}}。
颜色是 hsla(H, S%, L%, A) 或 #RRGGBB，**必须自己转换**。

三层解析：联网缓存 → 安装目录 → 内置占位。
联网只覆盖 6 个键：appIcon / appTaskbarIcon / background / logo / button（button.backgroundImage）/ buttonFont（button.fontPath）；颜色 / 悬停色 / 字号 / 字重 / 阴影**永远**取安装目录的 theme-settings.json。
（官方 `getAssets` 有 12 个素材描述符，多出 buttonHover / buttonDisabled / buttonText 三组背景图与字体；本项目暂只实现上述 6 个，其余仍按安装目录渲染。）

**按钮渲染语义（2026-10-27 修正，之前一直理解错）**：`button.color` 是**文字色**（官方 schema 原文 `Color of font on element`），底色只由 `button.backgroundImage` 提供；`buttonHover.color` 是悬停时的文字色。代码里这两个字段叫 `ThemeSpec.ButtonTextColor` / `ButtonHoverTextColor`（旧名 `ButtonColor` / `ButtonHoverColor` 已废弃 —— 旧名会诱导后人继续把它当底色）。

官方尺寸 `min-width: 14.5rem` × `height: 4.1667rem` = **232×66 px**（按 16px 根字号）；背景默认 `background-size: auto 4.1667rem`（高固定 66.7px、宽自动、可重复），图自然高 >66px 时改成 `background-size: 100%`。本项目简化为 `Stretch=Fill` 一次铺满 —— 不裁掉 V3 那种带金珠边框的图。

状态语义：悬停 `filter: brightness(115%)`（本项目用 8% 白色叠加层，不改 Button.Background）、按下 `transform: scale(.85)`（`CompositeTransform`）、禁用 `opacity: .8 + grayscale(100%)` 且 `pointer-events: none`（本项目用 55% 透明 + 40% 文字色 + `IsEnabled=false`）。

未实现：`localizedImages`（play / resume / getStarted 各三态的整图按钮，**实测 4 个已装游戏都没用**）、`buttonDisabled` / `buttonText` 素材、`height`、`effect3dDisabled`、`hoverAnimationDisabled`。

合并顺序：联网缓存命中某键就用缓存文件，未命中回落安装目录；`ThemeSpec.Source` 三态 —— 有任意缓存 → OnlineCache、否则安装目录有任意图 → InstallDir、否则 BuiltInPlaceholder。

## 7. 联网主题子系统

POST https://api.paradox-interactive.com/metadata/graphql ，**匿名**，body：
{"operationName":"GetMetadata","query":"query GetMetadata($gameIds: [ID]) { games(ids: $gameIds) { id name themeSettings { appIcon appTaskbarIcon background button { backgroundImage color fontName fontPath fontSize fontWeight textShadow } buttonHover { backgroundImage color fontName fontPath fontSize fontWeight textShadow } buttonDisabled { backgroundImage color fontName fontPath fontSize fontWeight textShadow } buttonText { backgroundImage color fontName fontPath fontSize fontWeight textShadow } logo } alternativeExecutables { label { en zhhans } exeArgs exePath } browserDlcUrl browserModUrl dlcPath exeArgs exePath formatVersion } }","variables":{"gameIds":["ck3"]}}
headers：Content-Type: application/json、User-Agent: ParadoxLauncher/<ver>、x-pdx-launcher-version: <ver>。

返回的图片 URL 形如 https://metadata-assets.paradox-interactive.com/ck3/launcher-background-image-623e232b92afa1cbb5a8（**无扩展名**，Content-Type 不可信 —— 必须按 magic bytes 判型）。

缓存：<程序目录>\data\theme-cache\<gameId>-<assetKey>-<urlHash8>.<ext>（**绝不写 P社的 cache 目录**）。
`<urlHash8>` = 素材 URL 的 SHA-256 前 4 字节小写十六进制（8 个字符）。文件名带 URL hash 是有意为之：新版本换了图 → URL 变 → 文件名变 → BitmapLoader / WinUI / FontLoader 里以路径为键的图片与字体缓存自动失效，不需要额外的失效通知。
索引：同目录 `<gameId>.assets.json`，记录每个键的 url / fileName 与 `lastCheckedUtc`（**主题的「上次检查时间」只写这里，不写 settings.json**）。
TTL **5 天**（432,000,000 ms，与官方 `pve=1e3*60*60*24*5` 一致）；URL 未变且文件存在则跳过下载。扩展名/文件名变化时删除同一键的旧文件。
下载：流式写 <target>.part → 原子改名；失败/取消删 .part 并保留旧图；并发 2；可取消。
白名单域名：metadata-assets.paradox-interactive.com、store.steampowered.com、api.paradox-interactive.com、braze-images.com（官方公告图，见 7.3；**只放行主域，子域一律拒绝**）。
magic：PNG 89 50 4E 47、JPEG FF D8 FF、GIF "GIF8"、SVG 含 "<svg"、TTF 00 01 00 00、WOFF wOFF、WOFF2 wOF2。拒绝 <1 KB 或 >40 MB（阈值取 1 KB 而非 4 KB：官方 cache 实测 ck3-buttonBackgroundImage 只有 3,818 B、stellaris 的 4,495 B，都是合法 PNG）。

实测：代理 127.0.0.1:7890 可用，HTTP 200，Content-Length 1851934 恰好等于本机 victoria3-background 字节数；**下载速度 ~10 KB/s**（1.8 MB 约 2 分钟）。

### 7.1 代理配置（`data\settings.json`）

| 键 | 取值 | 说明 |
| --- | --- | --- |
| `proxyMode` | `"direct"` / `"system"` / `"manual"` | 缺省（旧配置）时按 `proxyUrl` 是否有值推断：有→`manual`，无→`direct` |
| `proxyUrl` | `"http://127.0.0.1:7890"` | 只在 `manual` 下使用；缺 scheme 自动补 `http://`，两端引号会被去掉 |

- `direct`：不构造代理，环境变量与 WinINET 设置都不读。
- `system`：读 `HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings` 的 `ProxyEnable`(`dword` 1/0)、`ProxyServer`、`ProxyOverride`、`AutoConfigURL`。
  `ProxyServer` 支持单值 `host:port`，也支持 `http=a:1;https=b:2` 分号写法（取 `http` 那一份，两侧不同会提示）。
  `ProxyEnable=0` 或没配时回落 `http_proxy` / `https_proxy` / `all_proxy`（含全大写）；检测到 `AutoConfigURL` 一律直连（.NET 不执行 PAC）。
- `manual`：只用 `proxyUrl`；只接受 `http`/`https`，SOCKS 与其它 scheme 会直连并写日志原因。
- `ProxyOverride` 的 `<local>` 映射成 `WebProxy.BypassProxyOnLocal`，其余项转成锚定正则（`*`→`.*`、其余字符转义）交给 `WebProxy.BypassList`。
- 地址里带 `user:pass@` 时解析成 `NetworkCredential` 交给 `WebProxy.Credentials`；handler 另外设 `DefaultProxyCredentials = CredentialCache.DefaultCredentials` 以应对 407 质询。

未安装游戏的封面：https://store.steampowered.com/api/appdetails?appids=<id> → header_image / capsule_imagev5，缓存 theme-cache\steam-store-thumbnails\<appid>，TTL 7 天。

### 7.2 DLC 简介（可选联网）

Victoria 3 的 `*.dlc.json` 自带 `description.en`，本地即可显示；**CK3 / Stellaris 的 `*.dlc` 完全没有简介字段**（已核对全部 25 + 31 个文件），要显示简介必须联网：

    GET https://store.steampowered.com/api/appdetails?appids=<steam_id>&l=<language>
      → data.short_description（为空时退 detailed_description 并去 HTML）→ 压空白、截断 400 字
      → success:false（已下架 / 区域不可用，例如 Stellaris 的 447680）与解析失败都写空文本做负缓存

`l=` 由界面语言推出（`SteamStoreLanguage.FromUiLanguage`）：中文界面 `schinese`，英文界面 `english`。**必须一个 appid 一次请求**：`appids=A,B` 逗号批量会 400 且 body 为 `null`；也不要加 `filters=`——`filters=short_description` 会 200 但 `data` 整个缺失，等于把简介写成负缓存。

缓存 `<程序目录>\data\dlc-descriptions.json`（`CacheVersion = 2`）：

    {
      "version": 2,
      "lastFailureUtc": null,
      "languages": {
        "schinese": { "<steamId>": { "text": "...", "fetchedUtc": "2026-10-27T03:00:00+08:00" } },
        "english":  { ... }
      }
    }

- **按商店语言分桶**：切界面语言不会把另一种语言的文本当成自己的缓存，也不会互相覆盖。
- v1 的扁平 `entries` 读到后自动迁移进 `schinese` 桶并回写一次（`MigrateLegacyEntries`）。
- 正缓存天数由设置项 `dlcDescriptionCacheDays`（1..365，默认 **7**）在每次调用时传入，**不落进缓存文件**——改天数立即对下一次读取生效。
- 并发 3（`MaxConcurrentRequests`）；首批网络错误即 `abort` 放弃本批，并写 `lastFailureUtc`，30 分钟内不再联网（`FailureBackoff`）。
- 过期条目先进结果再后台刷新（先用旧文本渲染）；空文本（下架/解析失败）同样按正缓存天数做负缓存，重启前不会重复请求。
- 失败只影响简介显示，不挡住列表；设置页「联网获取 DLC 简介」关闭后只用本地描述 + 已有缓存。

UI：DLC 列表左侧固定 208×117 缩略图（尺寸写死在 Border 上，文字多长都不会被压小），右侧标题（最多 2 行）+ 简介（次要色，最多 2 行，无简介时整行折叠，ToolTip 显示全文）。

### 7.3 官方公告（Braze content cards，可选联网）

官方启动器底部那几张公告卡不是本地资源，而是 Braze content cards：

    GET https://api.paradox-interactive.com/communication/braze/contentcards/<gameId>
      → 顶层按 slot 分组：{ "main": {...}, "secondary-1": {...}, "secondary-2": {...} }

- slot 名与顺序固定为 `main` → `secondary-1` → `secondary-2`（对应官方渲染常量 `MAIN` / `SECONDARY1` / `SECONDARY2`）；每个 slot 下是 `contentGroups[]`。
- 每组 `settings.visible.from` / `visible.until`（ISO 8601）限制时间窗，`settings.weight`（缺省 1）决定同 slot 内优先级：**取当前时间可见且 weight 最大的组，同 weight 取先出现的**；组内按顺序取第一条有内容的 `contentItems[]`（官方每 slot 上限 8 条）。
- 条目内容在 `contentItems[0].content`：`title` / `text` 可能是字符串，也可能是 `{en, zhhans, ...}` 的多语言对象（**优先 `en`，没有 en 就取任意语言**；实测官方标题/正文均为空串，卡片只有图）；`image.src`（也接受 `cacheSrc`、`backgroundImage` 里的 css `url(...)`）；`link` 可能是字符串或 `{href}`。
- 四个字段（title / text / image / link）全空的条目视为不存在 —— 该 slot 等于没有公告。

实测（2026-10）：`ck3` / `stellaris` / `eu5` / `hoi4` / `imperator_rome` / `aow4` 返回 200 且有内容；`victoria3` / `eu4` / `millenia` / `cities_skylines_2` 返回 **404**（顶层也可能是 `{}`）。CK3 三 slot 各 1 组，`visible.from`=2024-01-01、`until`=2099-12-31（等同常显），`contentType`="image+button"，`secondary-2` 的 link 是深链 `pdxlaun://accountonboarding`。

主页渲染对齐官方 `.news`：三块固定尺寸的**纯图块** —— main 451×230、两个 secondary 各 246×230，间距 21；圆角 4、外圈 1px 黑 40%（`#66000000`）+ 内圈 1px 白 10%，**没有内层卡片、没有内边距**；悬停叠 50% `#131724` 遮罩；只有 `title` 非空时才在左下角叠 24px 白字 + 180° 底部渐变（正文不渲染，官方也不渲染）。图用 WinUI `Image` + `UniformToFill` 等价实现 CSS `background-size:cover`。

缓存（数据目录，`PdxDataRoot`）`news-cache\news-cards.json`：`{version, entries: {<gameId>: {fetchedUtc, lastFailureUtc, lastImageFailureUtc, items: [{slot, title, text, imageFileName, imageUrl, link}]}}}`；图片落在同目录 `images\<md5(url) 小写十六进制>.<ext>`（**用整条 URL 的 MD5，不用接口里那个会随内容变的 hash 段**，命中即复用，不重下）。`lastImageFailureUtc` 是后加的可空字段，旧缓存文件没有它也能读。

- TTL **6 小时**；HTTP 404 视为「该游戏没有公告」并写空条目做**负缓存**（同样 6 小时），不算错误、不告警。
- 失败（网络 / JSON / IO）保留上次内容并记 `lastFailureUtc`，**30 分钟内不再重试**；失败只影响公告区，主页其余部分照常。
- 公告图（`braze-images.com`）在部分网络下直连不可达，所以单张图限时 **8 秒**：超时即视为图片域不可用，本轮**不再尝试剩余图片**（避免每张都干等）；此时**不推进 `fetchedUtc`**（6 小时 TTL 不生效），只写 `lastImageFailureUtc`，**5 分钟后自动重试**补图。图没取到的卡都是纯图卡（标题/正文为空）→ 主页按空卡隐藏，图补齐后自动出现。
- 图片快速失败（404 / 域名不在白名单 / magic 不识别）只丢那一张图，其余照常，仍按成功缓存 6 小时。
- 设置页「联网获取官方公告」关闭后**绝不发起任何请求**，只读本地缓存；没有缓存就整块隐藏公告区。
- 深链 `pdxlaun://` 不交给浏览器（不是 http/https）：卡片**不置灰**（官方卡也没有禁用态），点击时在公告区下方弹一行「当前版本暂不支持跳转」的提示。
- 已知限制：GIF（main 图是 .gif）只显示首帧 —— `BitmapImage` 不播放动画。

## 8. 启动

    exePath 绝对化 = Path.GetFullPath(Combine(InstallRoot, launcherSettings.exePath))
    cwd          = Path.GetDirectoryName(exePath)          // exe 自己所在目录
    args         = launchRequest 的额外参数（无登录时为空） + launcherSettings.exeArgs
    启动方式     = detached，stdio 全部不重定向（stdin/stdout/stderr 都不捕获）

官方启动前会先写加载文件（我们改为 UI 里显式保存时写，启动时读当前状态）。

--continuelastsave 只在 isGameContinued 时加。

## 9. dlc_signature（默认不写）

md5( pad255(normalizedMachineId) + 按 name 排序的每个 dlc.rawBytes + "DontStealMyGamePlz__WINNERS_DONT_USE_DRUGS__DONT_COPY_THAT_FLOPPY\0" ) → 32 位小写 hex，UTF-8 **无换行无 BOM**。
normalizedMachineId = MachineGuid（HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid，本机 96e94400-f07f-440d-9979-6a1e82468b15）经"每字符后插 NUL"变换（72 字符），再 Buffer.alloc(255).write()。
已用 Stellaris 验证可复现：812abb4563023f364077c823548eb05b。CK3 的盘上值无法复现（历史版本写的）⇒ **默认不写**，只在设置页提供"手动重算"按钮。

## 10. 文件归属

| 文件 | 谁写 | 启动必需 |
| --- | --- | --- |
| dlc_load.json / content_load.json | **启动器** | **是** |
| dlc_signature | 启动器 | 仅 Steam/PDX DRM 校验 |
| game_data.json | 启动器 | 否（游戏不读） |
| continue_game.json | **游戏** | 否（启动器只读，用于"继续游戏"展示） |
| pdx_settings.txt | 双方 | 否（游戏自建） |
| mods_registry.json | 旧启动器 | 否（只读遗留） |
| playsets.json（EU5） | **游戏** | 否（官方启动器从不写） |

## 11. Prison Architect（支持等级：仅启动）

本机实测（F:\SteamLibrary\steamapps\common\Prison Architect，appid 233450）：

- `Launcher\launcher-settings.json`：gameId `prison_architect_pc_console`、formatVersion **1**、exePath `../Prison Architect64.exe`、
  exeArgs `["--launched-from-launcher"]`、dlcPath `"./"`、gameDataPath `%USERPROFILE%/AppData/Local/Introversion/Prison Architect`。
- 游戏数据目录（gameDataPath）在本机**不存在**（游戏从未启动过），也没有 `launcher-v2.sqlite`。
- 本体 exe 内**没有** `content_load` 字样 ⇒ 它不读启动器写的加载文件。
- 安装目录下**没有任何** `*.dlc` / `*.dlc.json` ⇒ DLC 只由 Steam 授权控制，扫目录无意义。
- mod 走 `gameDataDir\mods\<name>\manifest.txt`（复数 mods 目录），与 PDX 的 `<gameDataDir>\mod\*.mod` 不同构。

⇒ 结论：**只负责启动**。启动器不读它的 sqlite、不写加载文件、不扫 DLC 目录；
播放集 / Mod / DLC 由游戏本体管理（游戏内 MOD 菜单 / Steam 创意工坊），UI 上对应导航项置灰。

## 12. 本启动器数据根目录（`PdxDataRoot`）

- 规则：**全部软件数据都在程序目录下**，不写 `%LOCALAPPDATA%`。解析顺序：
  `<exe 目录>\data-root.txt`（文件里写一行绝对路径，逃生口）→ `<exe 目录>\data` →
  `<exe 目录>\..\PdxNext-data`（程序目录不可写时）；全不可写仍返回 `<exe 目录>\data`，
  由上层降级（日志退化成 Debug.WriteLine），**绝不回退 C 盘**。
- 布局：`data\settings.json`、`data\logs\pdxnext.log`、`data\theme-cache\`、`data\news-cache\`、`data\mod-cache\`、`data\dlc-descriptions.json`。
- 迁移：首次启动若目标 `data` 里既没有 `settings.json` 也没有 `.migrated-from-localappdata`，
  就把旧 `%LOCALAPPDATA%\PdxNext` 的 settings / dlc-descriptions / theme-cache / news-cache / mod-cache
  **复制**过来（不搬 logs、永不删源、已存在的文件跳过）。设置页「数据目录」卡片显示绝对路径、
  提供「打开目录」与「清理旧 C 盘数据」（只删 `%LOCALAPPDATA%\PdxNext`）。
- 发布：`publish.cmd` 覆盖 `dist\PdxNext` 与暂存目录前都会把 `data` 搬到 `*.__data__`，发布后搬回。

## 13. 界面档位（背景显示方式 / Mod 缩略图大小）

`data\settings.json` 里两个纯显示档位，都只影响 UI 渲染；不认识的值经 `Normalize` 回退到默认档，不会报错。

| 键 | 取值 | 缺省回退 | 作用 |
| --- | --- | --- | --- |
| `backgroundDisplayMode` | `cover` / `fill` / `fullBlur` / `fullPlain` / `center` | `cover` | 整窗背景（`ShellBackgroundRect` + `ShellBackgroundBrush`）的铺图方式 |
| `modThumbnailSize` | `small` / `medium` / `large` | `medium` | Mod 库 / Mod 页左侧缩略图边长：32 / 48 / 72 DIP |

背景五档 → `ImageBrush` 的换算（`src/PdxLauncher.App/Services/BackgroundDisplayModeCatalog.cs:55-70`，AlignmentY 一律 `Center`）：

| 档 | 中文名 | Stretch | AlignmentX | 模糊铺底层 | 视觉 |
| --- | --- | --- | --- | --- | --- |
| `cover` | 覆盖（默认） | UniformToFill | Left | 无 | 铺满整窗，超出部分左对齐裁掉 |
| `fill` | 填充 | Fill | Center | 无 | 强行拉伸铺满，比例失真 |
| `fullBlur` | 完整（模糊底） | Uniform | Center | 有（`ShellBackgroundBlurRect`，96 px 宽解码后拉伸） | 完整图居中，空白处用同一张模糊图补，无纯色边 |
| `fullPlain` | 完整（纯色边） | Uniform | Center | 无 | 完整图居中，空白处露出 `PdxWindowBackgroundBrush` 底色 |
| `center` | 居中（原始像素） | None | Center | 无 | 1:1 原始像素居中，超出窗口的部分裁掉 |

- 遮罩（`ShellScrim`）只跟「有没有背景图」联动：没有图就隐藏、窗口退回纯色底，与档位无关。
- 切档**不重新解码大图**，只改 `ImageBrush` 的 Stretch / 对齐；`fullBlur` 首次需要模糊铺底时才懒加载那 96 px 的副本。
- 设置页改档立即重画（`MainWindow.OnBackgroundDisplayChanged` 派发到 UI 线程），重启后从 settings.json 恢复。

Mod 缩略图：`ModThumbnailSizes.Pixels()` 返回 DIP（32 / 48 / 72），XAML 里按系统缩放换算；150% DPI 实测行高 55 / 72 / 108 px。联网缩略图缓存（`mod-cache\mod-thumbnails`）与档位无关，只受缓存天数与尺寸上限约束。

导航顺序固定为：当前游戏 → 播放集 → Mod 库 → DLC → 游戏库（`MainWindow.xaml:157-205`，AutomationId `navHome` / `navPlaySets` / `navModLibrary` / `navDlc` / `navLibrary`），设置入口固定在左下角（`navSettings`）。


## 14. 界面语言与多语言文案（i18n）

界面文案全部来自 `tools/i18n/<language>/<area>.json`，**代码里不再手写界面文字**（日志与注释仍用中文，不进文案表）。

- 语言只有两档：`zh-CN`（简体中文，默认）与 `en-US`（English）。`data\settings.json` 的 `uiLanguage` 缺失或为 `null` 表示**跟随系统**（系统 UI 语言是 `zh*` 就用中文，其余用英文）；写了不支持的值按 `zh-CN` 处理。
- 启动时在 `App.OnLaunched` 里 `ComposeServices()` 之后、任何页面 XAML 加载之前 `Localizer.Instance.Apply(...)`；设置页改语言**立即生效，不需要重启**。
- 只改 `CultureInfo.DefaultThreadCurrentUICulture`，不动 `CurrentCulture`（数字/日期格式不跟着变）。

区域文件（键前缀 = 文件名，各区域键集在两种语言之间必须完全一致）：

| area | 覆盖范围 |
| --- | --- |
| `common` | 通用按钮/状态（确定、取消、关闭、清空、复制路径、`{0} 天`、`{0} 个文件`…） |
| `settings` | 设置页全部卡片（主题 / 代理 / 界面 / 启动 / 数据 / 程序数据目录 / Mod 缩略图 / 缓存 / 危险区）与下拉选项说明 |
| `shell` | 外壳：导航项、窗口置顶、版本号、播放集保存/校验错误 |
| `home` / `library` | 当前游戏页与游戏库 |
| `mods` / `modlib` | Mod 页与 Mod 库页 |
| `dlc` | DLC 页 |

生成流程：

    python tools/gen-i18n.py            # 合并两种语言的各 area → 生成 Strings.g.cs
    python tools/gen-i18n.py --check    # 只校验：键集一致 + 值合法 + 生成结果最新（不过期）
    bash tools/build-app.sh             # 先跑生成器再 dotnet build（Windows 控制台需 PYTHONIOENCODING=utf-8 否则中文输出乱码）

- 生成物 `src/PdxLauncher.App/Localization/Strings.g.cs` 是机器产物**不要手改**：每个键映射成一个 `Localizer` 只读属性（`app.name` → `AppName`）与一个 `Keys.Xxx` 常量（ViewModel 里 `T(Keys.Xxx, args)` 用），切换语言时对每个键发一次 `PropertyChanged`，XAML 全部用 `{x:Bind L.Xxx, Mode=OneWay}` 绑定。
- 值里允许 `{0}`/`{1}` 占位（两种语言的占位序号必须一致）；不允许首尾空格、不允许空值；段名只能是小写字母开头的标识符片段。
- ViewModel 继承 `LocalizedObservableObject` 即可在切语言时自动给自身公开属性补发通知（`Localizer` 只存**弱引用**，不会钉住已关闭页面的 ViewModel）；需要重算缓存文本的覆盖 `Relocalize()`。
- 代码里拼出来的文案（InfoBar、对话框、状态栏）不要存成品字符串：存 `(key, args)` 再 `T(...)`，否则切语言后残留旧语言。

## 15. 缓存管理与可清理范围

设置页「缓存」卡片列出四行，每行显示 `<占用字节>，<文件数>`、路径与「清空」按钮；标题栏汇总给出总占用，并提供「刷新统计」与「清空全部缓存」。占用是**每次进入设置页/点刷新时现算**（`CacheStorage.Measure` 遍历目录累加 `GetFileSize`），不做后台跟踪。

| key | 路径 | 清空动作 |
| --- | --- | --- |
| `theme` | `data\theme-cache`（元数据下发的背景/字标，见第 7 节） | 删整个目录；下次启动按 TTL 重新拉取 |
| `news` | `data\news-cache`（Braze 公告 JSON + 公告图） | 删缓存文件与公告图目录，并丢弃内存副本 |
| `modThumbnails` | `data\mod-cache`（索引 + 缩略图文件） | 清索引表后删索引与图片目录 |
| `dlcDescriptions` | `data\dlc-descriptions.json`（见 7.2） | 删缓存文件；内存里的负缓存随之失效 |

- 清理是**尽力而为**：文件被占用/无权限时只写 Warn 日志，界面提示「清理失败」，不会抛到 UI。
- 清理后立即重算该行与总计；状态栏给出「已清空 <行名>，释放 <大小>」。
- 「清空全部缓存」需要二次确认（ContentDialog），只删上表四个位置，**不动** `settings.json`、`logs`、游戏安装目录与存档。
- 这四行与「Mod 缩略图缓存天数」「DLC 简介缓存天数」相互独立：清空不会改设置，改设置也不会立刻删已有缓存（过期判定在读取时进行）。
