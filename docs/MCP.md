# MCP：跨页只读与受控变更计划

PdxNext 附带一个独立的 stdio MCP 程序 `PdxLauncher.Mcp.exe`，让 Claude Desktop、Cursor
等 AI 客户端读写 **Mod 上传表单草稿**，并以最小权限读取播放集与设置页背后的业务数据。
AI 对业务数据的写入只能提交 **变更计划**；用户必须在启动器「设置 → MCP → AI 变更计划」里
核对并批准，实际写入由运行中的 App 执行。MCP 没有直接执行业务变更的工具，也不会发布 Mod。

## 1. 组成

| 位置 | 作用 |
| --- | --- |
| `src/PdxLauncher.Mcp/` | 独立控制台程序，手写 JSON-RPC 2.0 / MCP stdio，不依赖 MCP SDK |
| `src/PdxLauncher.Core/ModStudio/ModUploadDraft.cs` | 草稿数据模型 |
| `src/PdxLauncher.Core/ModStudio/ModUploadDraftStore.cs` | 草稿文件存储（原子写） |
| `src/PdxLauncher.App/Controls/McpDocsContent.xaml(.cs)` | 设置页 MCP 分节里的文档与配置复制 |
| `src/PdxLauncher.Core/Mcp/` | 工具目录、调用记录模型与 SQLite 存储 |
| `src/PdxLauncher.Core/Mcp/McpAccessKeyStore.cs` | 本地访问密钥（只存哈希与前缀） |
| `src/PdxLauncher.Core/Mcp/McpClientConfigGenerator.cs` | 多客户端连接配置生成 |
| `src/PdxLauncher.Core/Mcp/McpChangePlan*.cs` | 变更计划模型、SQLite 存储、字段白名单与 App 执行器 |
| `src/PdxLauncher.Core/Mcp/PlaySetStoreRouter.cs` | MCP 与页面共用的播放集仓储路由 |
| `docs/MCP.md` | 本文件 |

`PdxLauncher.Mcp.exe` 由 App 的构建目标复制到 App 输出目录；`publish.cmd` 和安装包
会把它一起带上。它不启动 WinUI，也不加载 Steam，只引用 `PdxLauncher.Core`。

入口在「设置 → MCP」分节：页面含权限开关、工具清单、一句话安装、三步流程和默认收起的手动 JSON 配置。
Mod 创建页不再保留 MCP 按钮。

## 2. 客户端配置

MCP 分节首屏的「一句话安装」可以直接复制给 AI 客户端（Claude Desktop / Claude Code /
Codex / Cursor / VS Code）；安装说明默认折叠，页面首屏只显示连接状态与权限。

```text
请把 PdxNext 的 MCP 服务接入当前客户端：
名称 pdxnext，命令 "<程序目录>\PdxLauncher.Mcp.exe"，
参数 "--data-root" "<程序目录>\data"；
       "--enabled" "true" "--draft-write" "false"；
接入后调用 list_games 验证。
```

点「手动配置（JSON）」会展开完整 JSON 和当前程序路径；JSON 的 `args` 会带上当前权限开关。

MCP 程序与 `PdxLauncher.App.exe` 在同一目录。启动器会按本机实际路径生成配置，
不是写死的：dev 构建指向 `bin` 输出目录，安装版指向安装目录。

```json
{
  "mcpServers": {
    "pdxnext": {
      "command": "C:\\Program Files\\PdxNext\\PdxLauncher.Mcp.exe",
      "args": ["--data-root", "C:\\Users\\<user>\\AppData\\Local\\PdxNext-Data",
               "--enabled", "true", "--draft-write", "false"]
    }
  }
}
```

- Claude Desktop：`%APPDATA%\Claude\claude_desktop_config.json`
- Cursor：项目或用户的 `mcp.json`
- Codex：`~/.codex/config.toml` 的 `[mcp_servers.pdxnext]`（页面会生成 TOML，不是 JSON）
- VS Code：`settings.json` 的 `servers` + `"type": "stdio"`
- 其它客户端：只要支持 stdio MCP，把它当普通 `command` 启动即可
- 改完配置要重启客户端，工具列表里应出现 `pdxnext`；工具数取决于权限：
  只读 7 个，开启草稿写入后 10 个

`--data-root` 可省略；省略时使用 MCP 程序自己解析出的数据根（与启动器同目录时结果一致）。
如果程序被复制到别处，必须显式传 `--data-root`。

## 3. 工具

所有工具都只操作本地数据，不发布、不改游戏目录、不调用 Steam。

### `list_games`

无参数。返回已安装游戏：

```json
{"games":[{"gameId":"victoria3","displayName":"Victoria 3","version":"1.9.x","installRoot":"...","steamAppId":529340,"canUpload":true}]}
```

### `list_mods`

参数：`gameId`。返回该游戏下能读描述符、可上传的本地目录型 mod：

```json
{"gameId":"victoria3","mods":[{"modId":"...","name":"...","dirPath":"...","folderName":"...","version":"...","supportedVersion":"...","tags":["Gameplay"],"descriptorFamily":"Metadata","canUpload":true}]}
```

### `list_tags`

参数：`gameId`。返回上传时可选的标签**机器名**（发给 Steam 的值）；界面会按当前语言
显示译文。Mod Studio 标签目录是 `ModTagCatalog`，译文映射是 `WorkshopTagKeys`。

### `get_upload_draft`

参数：`gameId`、`modPath`（来自 `list_mods`）。

- 有草稿：返回 `{"exists":true,"draft":{...}}`
- 没有草稿：返回 `{"exists":false,"defaults":{...}}`，默认值来自描述符

### `save_upload_draft`

参数：`gameId`、`modPath`，以及可选的 `title`、`description`、`tags`、`visibility`
（`public` / `friends` / `private` / `unlisted`）、`changeNote`、`contentDescriptors`
（1-5 的整数数组）。支持部分更新，未传的字段沿用现有草稿或描述符默认值。

校验规则与启动器上传一致：

- 标题非空，≤ 128 UTF-8 字节
- 描述、更新说明 ≤ 8000 UTF-8 字节
- 标签必须是该游戏 `list_tags` 返回的标签，或描述符里已有的标签
- 内容描述词只接受 1-5

### `clear_upload_draft`

参数：`gameId`、`modPath`。删除草稿，返回 `{"ok":true,"deleted":true|false}`。

### `mcp_describe_capabilities`

无参数。返回当前 MCP 版本支持的作用域、页面映射和写入模式；不读取用户数据。

### `get_playsets_page`

参数：`gameId`（可选）、`offset`、`limit`。返回播放集页的业务快照：
`id`、`name`、`isActive`、`expectedHash`，以及 `totalCount`、`hasMore`、`nextOffset`。
读取路径复用启动器的播放集仓储路由，官方库、EU5 JSON、CS2 JSON 和自有库口径一致。

### `get_settings_page`

参数：`offset`、`limit`。只返回设置白名单字段（界面语言、播放集显示方式、置顶、翻译开关等）。
MCP 权限、代理地址、翻译 API key、账号信息、窗口位置等字段永远不进入快照。

### `create_change_plan`

参数：`domain`、`operation`、`gameId`、`targetId`、`expectedHash`、`patch`。
`settings` 域的 `gameId` 必须为空字符串；`playsets` 域必填，传空会被拒绝。
只创建计划并写入自有 SQLite，不经 App 批准不会改业务数据。首期支持：
`playsets/rename_play_set`、`playsets/create_play_set`、`playsets/set_mod_enabled`、
`settings/set_setting`；危险操作（删除、激活、发布、账号、补丁）不在白名单内。
成功后返回 `status: "awaiting_ui_approval"` 与 `planId`。

## 4. 草稿文件

路径：

```
<data>\mcp\upload-drafts\<gameId>\<mod 路径 SHA-256 前 16 位>.json
```

示例：

```json
{
  "schemaVersion": 1,
  "gameId": "victoria3",
  "modPath": "C:\\Users\\me\\Documents\\Paradox Interactive\\Victoria 3\\mod\\my_mod",
  "title": "My Mod",
  "description": "[b]...[/b]",
  "tags": ["Gameplay", "Translation"],
  "visibility": "Public",
  "changeNote": null,
  "contentDescriptors": [],
  "updatedAt": "2026-11-08T12:00:00+00:00"
}
```

- 写入走临时文件 + rename（`IFileSystem.WriteAllTextAtomic`），启动器不会读到半截 JSON
- 路径哈希忽略大小写和尾分隔符；`gameId` 用 `KnownGames.CanonicalizeGameId` 折成正名
- 启动器只在草稿比 `ModPublication.UpdatedAt` 新时才应用，避免旧草稿覆盖已发布内容
- 启动器上传成功后删除对应草稿

## 5. 启动器侧的交互

- 进入上传面板（`ShowUpload`）时静默应用最新草稿；没有草稿不提示
- 上传面板每秒轮询一次草稿文件；MCP 保存后，用户不点任何按钮就能看到 AI 写入的标题、描述、标签和可见性
- 页面重建并恢复到上传面板时，恢复页面状态后再应用一次最新草稿
- 草稿比上次上传旧时提示并忽略（`UploadDraftNoticeKind.Outdated`）
- 应用草稿只改标题、描述、更新说明、可见性、标签、内容描述词；预览图、附加语言、
  Mod ID、目录保持原样
- 草稿标签会重新生成复选框；描述符里有、目录里没有的标签会追加显示

## 6. 安全边界

- 每次 `tools/call` 只在 `data\pdxnext.sqlite` 的 `mcp_calls` 表记录元数据（时间、耗时、工具名、结果、权限模式），最多 500 条；不记录参数、路径、标题或描述
- 调用历史写入失败只记日志，不改变工具响应；设置页“最近调用”只显示最近 100 条
- 调用历史可按工具、结果和密钥筛选，分页浏览，清空或导出 JSON；导出只含元数据白名单
- 访问密钥存于 `mcp_access_keys` 表：明文 `pdxmcp_<id>_<secret>` 只在创建/轮换时显示一次，SQLite 只存 SHA-256 哈希与前缀；校验使用固定时间比较
- 无 `--access-key` 是兼容模式，沿用全局 `--enabled` / `--draft-write`；传密钥时该密钥的工具范围生效，过期/禁用/错误密钥被拒绝
- Claude Desktop 配置内联密钥；Claude Code、Codex、Cursor、VS Code 配置用 `PDXNEXT_MCP_ACCESS_KEY` 占位，用户自行写入安全存储
- 传了密钥但校验失败时直接拒绝：工具表为空，`tools/call` 返回 `-32001`；只有完全不传 `--access-key` 才是兼容模式
- 总开关由 `AppSettings.McpEnabled` 控制（默认开）：关闭后 MCP 进程返回空工具表并拒绝全部 `tools/call`
- 工具权限按最小权限分层：只读工具（`list_games` / `list_mods` / `list_tags` / `get_upload_draft`）默认可用；
  写入工具（`save_upload_draft` / `clear_upload_draft`）必须由 `AppSettings.McpDraftWriteEnabled` 单独开启（默认关）
- 权限通过 `--enabled` / `--draft-write` 传给 MCP 子进程，并写进生成的客户端配置 args；改完需重启 AI 客户端
- 工具只能写入 `--data-root` 下的 `mcp\upload-drafts`，写入路径由游戏 id + mod 路径哈希推导
- 写草稿前要求 mod 能被 `list_mods` 发现（本地目录、描述符可读、游戏可上传）
- 没有发布工具，也不接受任意文件路径
- 日志写 `<data>\logs\pdxnext-mcp.log` 和 stderr；stdout 只输出 JSON-RPC，避免污染协议
- 变更计划表为 `mcp_change_plans`：MCP 只能推进到 `awaiting_ui_approval`；`approved` / `applied` / `failed` 只能由 App 写入
- App 执行计划前重新检查字段白名单、过期时间和 `expectedHash`；当前值不一致时拒绝执行，避免覆盖用户后续修改
- 设置写计划只接受白名单字段，MCP 权限、代理、Steam、翻译 API key、账号相关字段一律拒绝
- MCP 进程没有 `apply_change_plan` 工具；用户点击批准后才由 WinUI App 调用现有仓储 / 设置存储落盘

## 7. 构建与排错

- `cmd /c dev.cmd build` 后，App 输出目录应有 `PdxLauncher.Mcp.exe`、`.dll`、
  `.deps.json`、`.runtimeconfig.json`
- `dotnet publish` 通过 `AddWinUIXamlArtifactsToPublish` 把这 4 个文件带进 publish 目录
- 安装包 `[Files]` 递归复制 payload，不需要额外条目
- 工具列表为空 / `list_games` 返回空：确认 `--data-root` 是否正确、游戏是否已安装、
  `launcher-settings.json` 是否存在
- 改了 MCP 代码后如果客户端仍跑旧进程：关闭客户端再启动；重新构建时被占用的 exe
  可能无法覆盖
- 草稿写入失败：检查数据目录是否可写；权限问题会在日志里出现 `写入草稿失败`

## 8. 自检

`tests/PdxLauncher.Checks/ModUploadDraftStoreChecks.cs` 覆盖保存/读取/删除、路径归一、
gameId 别名、坏 JSON 降级和不同 mod 隔离。标签译文覆盖仍由
`WorkshopTagTextChecks` 保证（全部 `ModTagCatalog` 标签都有 resw 键）。
