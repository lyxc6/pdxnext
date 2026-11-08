# MCP：把上传表单交给 AI 编辑

PdxNext 附带一个独立的 stdio MCP 程序 `PdxLauncher.Mcp.exe`，让 Claude Desktop、Cursor
等 AI 客户端读写 **Mod 上传表单草稿**：标题、描述、标签、可见性、更新说明、内容描述词。
启动器打开上传面板时会把最新草稿填入表单；**发布仍然只能由用户在界面上确认**。

## 1. 组成

| 位置 | 作用 |
| --- | --- |
| `src/PdxLauncher.Mcp/` | 独立控制台程序，手写 JSON-RPC 2.0 / MCP stdio，不依赖 MCP SDK |
| `src/PdxLauncher.Core/ModStudio/ModUploadDraft.cs` | 草稿数据模型 |
| `src/PdxLauncher.Core/ModStudio/ModUploadDraftStore.cs` | 草稿文件存储（原子写） |
| `src/PdxLauncher.App/Views/McpDocsDialog.cs` | 上传面板里的应用内文档与配置复制 |
| `docs/MCP.md` | 本文件 |

`PdxLauncher.Mcp.exe` 由 App 的构建目标复制到 App 输出目录；`publish.cmd` 和安装包
会把它一起带上。它不启动 WinUI，也不加载 Steam，只引用 `PdxLauncher.Core`。

入口在 Mod 创建页顶部搜索框右侧的「AI 助手（MCP）」按钮：点开是导入步骤、可复制的
客户端配置和工具列表。

## 2. 客户端配置

MCP 程序与 `PdxLauncher.App.exe` 在同一目录。启动器上传面板的「AI 助手（MCP）」
按钮会按本机实际路径生成同样的 JSON：

```json
{
  "mcpServers": {
    "pdxnext": {
      "command": "C:\\Program Files\\PdxNext\\PdxLauncher.Mcp.exe",
      "args": ["--data-root", "C:\\Users\\<user>\\AppData\\Local\\PdxNext-Data"]
    }
  }
}
```

- Claude Desktop：`%APPDATA%\Claude\claude_desktop_config.json`
- Cursor：项目或用户的 `mcp.json`
- 其它客户端：只要支持 stdio MCP，把它当普通 `command` 启动即可
- 改完配置要重启客户端，工具列表里应出现 `pdxnext` 和 6 个工具

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

- 工具只能写入 `--data-root` 下的 `mcp\upload-drafts`，写入路径由游戏 id + mod 路径哈希推导
- 写草稿前要求 mod 能被 `list_mods` 发现（本地目录、描述符可读、游戏可上传）
- 没有发布工具，也不接受任意文件路径
- 日志写 `<data>\logs\pdxnext-mcp.log` 和 stderr；stdout 只输出 JSON-RPC，避免污染协议

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
