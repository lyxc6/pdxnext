namespace PdxLauncher.Mcp;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Games;
using PdxLauncher.Core.ModStudio;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Steam;

/// <summary>一个 MCP 工具的调用结果：协议层只关心“是不是错误 + 给 AI 看的文本”。</summary>
internal readonly record struct McpToolResult(bool IsError, string Text);

/// <summary>
/// MCP 工具实现：把上传表单草稿暴露给 AI。
/// <para>
/// 工具只读写草稿文件，不发布、不改描述符、不碰 Steam。所有写操作先经过
/// <see cref="ModDescriptorRules"/> / <see cref="ModTagCatalog"/> / <see cref="ModPublishLimits"/>
/// 校验，避免 AI 写出启动器上传时才会失败的草稿。
/// </para>
/// </summary>
internal sealed class McpTools
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly ModUploadDraftStore _drafts;
    private readonly GameCatalog _gameCatalog;
    private readonly ModCatalog _modCatalog;

    private IReadOnlyList<GameInstallation>? _gameCache;
    private readonly Dictionary<string, IReadOnlyList<ModCandidate>> _modCache =
        new(StringComparer.OrdinalIgnoreCase);

    public McpTools(IFileSystem fileSystem, IAppLogger logger, string dataRoot)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _drafts = new ModUploadDraftStore(fileSystem, dataRoot);
        _gameCatalog = new GameCatalog(fileSystem, logger);
        _modCatalog = new ModCatalog(fileSystem, logger);
    }

    /// <summary>tools/list 的返回。</summary>
    public JsonArray ListTools()
    {
        return new JsonArray
        {
            Tool(
                "list_games",
                "列出已安装的 Paradox 游戏；canUpload=true 的游戏支持本启动器上传到 Steam 创意工坊。",
                Schema(new JsonObject())),

            Tool(
                "list_mods",
                "列出某款游戏下的本地 mod（只含能读描述符、可上传的目录型 mod）。",
                Schema(
                    new JsonObject
                    {
                        ["gameId"] = Property("string", "游戏 id，例如 victoria3、ck3。"),
                    },
                    "gameId")),

            Tool(
                "list_tags",
                "列出某款游戏上传时可选的 Steam 标签机器名；界面会按当前语言显示译文。",
                Schema(
                    new JsonObject
                    {
                        ["gameId"] = Property("string", "游戏 id，例如 victoria3、ck3。"),
                    },
                    "gameId")),

            Tool(
                "get_upload_draft",
                "读取某个 mod 的上传草稿；没有草稿时返回描述符里的默认标题、描述与标签。",
                Schema(
                    new JsonObject
                    {
                        ["gameId"] = Property("string", "游戏 id。"),
                        ["modPath"] = Property("string", "mod 目录全路径，来自 list_mods。"),
                    },
                    "gameId",
                    "modPath")),

            Tool(
                "save_upload_draft",
                "写入（或部分更新）某个 mod 的上传草稿。只写草稿，不会发布；标题、描述、标签等会先按启动器的上传限制校验。",
                Schema(
                    new JsonObject
                    {
                        ["gameId"] = Property("string", "游戏 id。"),
                        ["modPath"] = Property("string", "mod 目录全路径，来自 list_mods。"),
                        ["title"] = Property("string", "工坊标题；不传则保留草稿/描述符里的值。"),
                        ["description"] = Property("string", "工坊描述，支持 BBCode。"),
                        ["tags"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject { ["type"] = "string" },
                            ["description"] = "标签机器名，先用 list_tags 查看可用值。",
                        },
                        ["visibility"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray("public", "friends", "private", "unlisted"),
                            ["description"] = "可见性。",
                        },
                        ["changeNote"] = new JsonObject
                        {
                            ["type"] = new JsonArray("string", "null"),
                            ["description"] = "更新说明；null 表示不写。",
                        },
                        ["contentDescriptors"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "integer",
                                ["minimum"] = 1,
                                ["maximum"] = 5,
                            },
                            ["description"] = "内容描述词 id：1 裸露 / 2 暴力 / 3 仅成人 / 4 频繁裸露 / 5 成人内容。",
                        },
                    },
                    "gameId",
                    "modPath")),

            Tool(
                "clear_upload_draft",
                "删除某个 mod 的上传草稿。",
                Schema(
                    new JsonObject
                    {
                        ["gameId"] = Property("string", "游戏 id。"),
                        ["modPath"] = Property("string", "mod 目录全路径。"),
                    },
                    "gameId",
                    "modPath")),
        };
    }

    /// <summary>tools/call 的分发。</summary>
    public async Task<McpToolResult> CallAsync(
        string name,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = name switch
            {
                "list_games" => await ListGamesAsync(cancellationToken).ConfigureAwait(false),
                "list_mods" => await ListModsAsync(arguments, cancellationToken).ConfigureAwait(false),
                "list_tags" => await ListTagsAsync(arguments, cancellationToken).ConfigureAwait(false),
                "get_upload_draft" => await GetUploadDraftAsync(arguments, cancellationToken).ConfigureAwait(false),
                "save_upload_draft" => await SaveUploadDraftAsync(arguments, cancellationToken).ConfigureAwait(false),
                "clear_upload_draft" => await ClearUploadDraftAsync(arguments, cancellationToken).ConfigureAwait(false),
                _ => throw new McpToolException("未知工具：" + name),
            };

            return new McpToolResult(false, result.ToJsonString(PrettyJson));
        }
        catch (McpToolException exception)
        {
            return new McpToolResult(true, exception.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error("MCP 工具 " + name + " 失败", exception);
            return new McpToolResult(true, "内部错误：" + exception.Message);
        }
    }

    private async Task<JsonNode> ListGamesAsync(CancellationToken cancellationToken)
    {
        var games = await GetGamesAsync(cancellationToken).ConfigureAwait(false);
        var array = new JsonArray();
        foreach (var game in games.OrderBy(static game => game.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            array.Add(new JsonObject
            {
                ["gameId"] = game.GameId,
                ["displayName"] = game.DisplayName,
                ["version"] = game.Version,
                ["installRoot"] = game.InstallRoot,
                ["steamAppId"] = game.SteamAppId is { } appId ? JsonValue.Create(appId) : null,
                ["canUpload"] = ModDescriptorRules.CanPublish(game.GameId),
            });
        }

        return new JsonObject { ["games"] = array };
    }

    private async Task<JsonNode> ListModsAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var game = await FindGameAsync(RequireString(arguments, "gameId"), cancellationToken).ConfigureAwait(false);
        var mods = await GetModsAsync(game, cancellationToken).ConfigureAwait(false);
        var array = new JsonArray();
        foreach (var mod in mods)
        {
            array.Add(new JsonObject
            {
                ["modId"] = mod.Entry.ModId.ToString(),
                ["name"] = mod.Descriptor.Name,
                ["dirPath"] = NormalizePath(mod.Entry.DirPath),
                ["folderName"] = mod.Descriptor.FolderName,
                ["version"] = mod.Descriptor.Version,
                ["supportedVersion"] = mod.Descriptor.SupportedVersion,
                ["tags"] = StringArray(mod.Descriptor.Tags),
                ["descriptorFamily"] = mod.Descriptor.Family.ToString(),
                ["canUpload"] = true,
            });
        }

        return new JsonObject
        {
            ["gameId"] = game.GameId,
            ["mods"] = array,
        };
    }

    private async Task<JsonNode> ListTagsAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var game = await FindGameAsync(RequireString(arguments, "gameId"), cancellationToken).ConfigureAwait(false);
        return new JsonObject
        {
            ["gameId"] = game.GameId,
            ["tags"] = StringArray(ModTagCatalog.For(game.GameId)),
            ["note"] = "这些是发给 Steam 的机器名；启动器界面会按当前语言显示译文。",
        };
    }

    private async Task<JsonNode> GetUploadDraftAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var game = await FindGameAsync(RequireString(arguments, "gameId"), cancellationToken).ConfigureAwait(false);
        var mod = await ResolveModAsync(game, RequireString(arguments, "modPath"), cancellationToken)
            .ConfigureAwait(false);
        var path = NormalizePath(mod.Entry.DirPath);
        var draft = _drafts.TryGet(game.GameId, path);
        if (draft is not null)
        {
            return new JsonObject
            {
                ["exists"] = true,
                ["draft"] = DraftJson(draft),
            };
        }

        return new JsonObject
        {
            ["exists"] = false,
            ["defaults"] = DraftJson(BuildDefaults(game, mod)),
            ["note"] = "还没有草稿；defaults 是描述符里的默认值。",
        };
    }

    private async Task<JsonNode> SaveUploadDraftAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var game = await FindGameAsync(RequireString(arguments, "gameId"), cancellationToken).ConfigureAwait(false);
        var mod = await ResolveModAsync(game, RequireString(arguments, "modPath"), cancellationToken)
            .ConfigureAwait(false);
        var path = NormalizePath(mod.Entry.DirPath);

        var existing = _drafts.TryGet(game.GameId, path);
        var baseDraft = existing ?? BuildDefaults(game, mod);

        var title = (OptionalString(arguments, "title", baseDraft.Title) ?? string.Empty).Trim();
        var description = OptionalString(arguments, "description", baseDraft.Description) ?? string.Empty;
        var changeNote = OptionalString(arguments, "changeNote", baseDraft.ChangeNote);
        var tags = OptionalTags(arguments, "tags", baseDraft.Tags);
        var visibility = OptionalVisibility(arguments, "visibility", baseDraft.Visibility);
        var descriptors = OptionalDescriptors(arguments, "contentDescriptors", baseDraft.ContentDescriptors);

        if (title.Length == 0)
        {
            throw new McpToolException("标题不能为空");
        }

        if (!ModPublishLimits.TryValidateTitle(title, out var titleError))
        {
            throw new McpToolException(titleError ?? "标题不合法");
        }

        if (!ModPublishLimits.TryValidateDescription(description, out var descriptionError))
        {
            throw new McpToolException(descriptionError ?? "描述不合法");
        }

        if (!ModPublishLimits.TryValidateChangeNote(changeNote, out var changeNoteError))
        {
            throw new McpToolException(changeNoteError ?? "更新说明不合法");
        }

        var allowedTags = AllowedTagMap(game, mod);
        var normalizedTags = new List<string>();
        foreach (var tag in tags)
        {
            if (!allowedTags.TryGetValue(tag, out var canonical))
            {
                throw new McpToolException(
                    "不支持的标签：" + tag + "；可用标签先用 list_tags 查看（gameId=" + game.GameId + "）。");
            }

            if (!normalizedTags.Contains(canonical, StringComparer.OrdinalIgnoreCase))
            {
                normalizedTags.Add(canonical);
            }
        }

        var draft = new ModUploadDraft(
            game.GameId,
            path,
            title,
            description,
            normalizedTags,
            visibility,
            string.IsNullOrWhiteSpace(changeNote) ? null : changeNote,
            descriptors,
            DateTimeOffset.UtcNow);

        if (!_drafts.Save(draft))
        {
            throw new McpToolException("写入草稿失败：" + _drafts.PathFor(game.GameId, path));
        }

        _logger.Info("MCP 已保存上传草稿：" + game.GameId + " / " + path);
        return new JsonObject
        {
            ["ok"] = true,
            ["path"] = _drafts.PathFor(game.GameId, path),
            ["draft"] = DraftJson(draft),
        };
    }

    private async Task<JsonNode> ClearUploadDraftAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var game = await FindGameAsync(RequireString(arguments, "gameId"), cancellationToken).ConfigureAwait(false);
        var mod = await ResolveModAsync(game, RequireString(arguments, "modPath"), cancellationToken)
            .ConfigureAwait(false);
        var deleted = _drafts.Delete(game.GameId, NormalizePath(mod.Entry.DirPath));
        return new JsonObject
        {
            ["ok"] = true,
            ["deleted"] = deleted,
        };
    }

    private async Task<IReadOnlyList<GameInstallation>> GetGamesAsync(CancellationToken cancellationToken)
    {
        if (_gameCache is not null)
        {
            return _gameCache;
        }

        _gameCache = await _gameCatalog.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        return _gameCache;
    }

    private async Task<GameInstallation> FindGameAsync(string gameId, CancellationToken cancellationToken)
    {
        var canonical = KnownGames.CanonicalizeGameId(gameId);
        var games = await GetGamesAsync(cancellationToken).ConfigureAwait(false);
        var game = games.FirstOrDefault(
            candidate => string.Equals(candidate.GameId, canonical, StringComparison.OrdinalIgnoreCase));
        if (game is null)
        {
            throw new McpToolException("没有找到已安装的游戏：" + gameId + "；先调用 list_games。");
        }

        if (!ModDescriptorRules.CanPublish(game.GameId))
        {
            throw new McpToolException("这款游戏不支持从本启动器上传到 Steam 创意工坊：" + game.GameId);
        }

        return game;
    }

    private async Task<IReadOnlyList<ModCandidate>> GetModsAsync(
        GameInstallation game,
        CancellationToken cancellationToken)
    {
        if (_modCache.TryGetValue(game.GameId, out var cached))
        {
            return cached;
        }

        var discovered = await _modCatalog.DiscoverAllAsync(game, cancellationToken).ConfigureAwait(false);
        var local = LocalModInventory.Merge(game, null, discovered, _fileSystem);
        var result = new List<ModCandidate>();
        foreach (var entry in local)
        {
            if (string.IsNullOrWhiteSpace(entry.DirPath) || !_fileSystem.DirectoryExists(entry.DirPath!))
            {
                continue;
            }

            if (!ModDescriptorReader.TryRead(game, entry.DirPath, entry.Name, _fileSystem, out var descriptor)
                || descriptor is not { IsReadable: true })
            {
                continue;
            }

            result.Add(new ModCandidate(entry, descriptor));
        }

        result.Sort(static (left, right) =>
            string.Compare(left.Descriptor.Name, right.Descriptor.Name, StringComparison.OrdinalIgnoreCase));
        _modCache[game.GameId] = result;
        return result;
    }

    private async Task<ModCandidate> ResolveModAsync(
        GameInstallation game,
        string modPath,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizePath(modPath);
        var mods = await GetModsAsync(game, cancellationToken).ConfigureAwait(false);
        var match = mods.FirstOrDefault(
            candidate => string.Equals(
                NormalizePath(candidate.Entry.DirPath),
                normalized,
                StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new McpToolException(
                "找不到本地 mod：" + modPath + "；先调用 list_mods（gameId=" + game.GameId + "）。");
        }

        return match;
    }

    private static ModUploadDraft BuildDefaults(GameInstallation game, ModCandidate mod)
    {
        var allowed = AllowedTagMap(game, mod);
        var tags = new List<string>();
        foreach (var tag in mod.Descriptor.Tags)
        {
            if (string.IsNullOrWhiteSpace(tag) || !allowed.TryGetValue(tag.Trim(), out var canonical))
            {
                continue;
            }

            if (!tags.Contains(canonical, StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(canonical);
            }
        }

        return new ModUploadDraft(
            game.GameId,
            NormalizePath(mod.Entry.DirPath),
            mod.Descriptor.Name,
            mod.Descriptor.Description ?? string.Empty,
            tags,
            SteamWorkshopVisibility.Public,
            null,
            Array.Empty<int>(),
            default);
    }

    private static Dictionary<string, string> AllowedTagMap(GameInstallation game, ModCandidate mod)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in ModTagCatalog.For(game.GameId))
        {
            map[tag] = tag;
        }

        // 描述符里已有的标签也允许保留，避免 AI 只改描述时把旧标签判成非法。
        foreach (var tag in mod.Descriptor.Tags)
        {
            var trimmed = tag?.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                map.TryAdd(trimmed, trimmed);
            }
        }

        return map;
    }

    private static JsonObject DraftJson(ModUploadDraft draft)
    {
        return new JsonObject
        {
            ["schemaVersion"] = draft.SchemaVersion,
            ["gameId"] = draft.GameId,
            ["modPath"] = NormalizePath(draft.ModPath),
            ["title"] = draft.Title,
            ["description"] = draft.Description,
            ["tags"] = StringArray(draft.Tags),
            ["visibility"] = VisibilityName(draft.Visibility),
            ["changeNote"] = draft.ChangeNote is null ? null : JsonValue.Create(draft.ChangeNote),
            ["contentDescriptors"] = IntArray(draft.Descriptors),
            ["updatedAt"] = draft.UpdatedAt == default
                ? null
                : JsonValue.Create(draft.UpdatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
        };
    }

    private static string RequireString(JsonObject arguments, string name)
    {
        if (arguments[name] is not JsonValue value
            || !value.TryGetValue<string>(out var text)
            || string.IsNullOrWhiteSpace(text))
        {
            throw new McpToolException("缺少或无效的参数：" + name);
        }

        return text.Trim();
    }

    private static string? OptionalString(JsonObject arguments, string name, string? fallback)
    {
        if (!arguments.TryGetPropertyValue(name, out var node))
        {
            return fallback;
        }

        if (node is null)
        {
            return null;
        }

        if (node is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            throw new McpToolException("参数 " + name + " 必须是字符串");
        }

        return text;
    }

    private static IReadOnlyList<string> OptionalTags(
        JsonObject arguments,
        string name,
        IReadOnlyList<string> fallback)
    {
        if (!arguments.TryGetPropertyValue(name, out var node))
        {
            return fallback;
        }

        if (node is null)
        {
            return Array.Empty<string>();
        }

        if (node is not JsonArray array)
        {
            throw new McpToolException("参数 " + name + " 必须是字符串数组");
        }

        var result = new List<string>();
        foreach (var item in array)
        {
            if (item is not JsonValue value
                || !value.TryGetValue<string>(out var text)
                || string.IsNullOrWhiteSpace(text))
            {
                throw new McpToolException("参数 " + name + " 里每一项都必须是非空字符串");
            }

            result.Add(text.Trim());
        }

        return result;
    }

    private static SteamWorkshopVisibility OptionalVisibility(
        JsonObject arguments,
        string name,
        SteamWorkshopVisibility fallback)
    {
        if (!arguments.TryGetPropertyValue(name, out var node))
        {
            return fallback;
        }

        if (node is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            throw new McpToolException("参数 " + name + " 必须是字符串");
        }

        return text.Trim().ToLowerInvariant() switch
        {
            "public" => SteamWorkshopVisibility.Public,
            "friends" or "friendsonly" => SteamWorkshopVisibility.FriendsOnly,
            "private" => SteamWorkshopVisibility.Private,
            "unlisted" => SteamWorkshopVisibility.Unlisted,
            _ => throw new McpToolException("可见性必须是 public / friends / private / unlisted"),
        };
    }

    private static IReadOnlyList<int>? OptionalDescriptors(
        JsonObject arguments,
        string name,
        IReadOnlyList<int>? fallback)
    {
        if (!arguments.TryGetPropertyValue(name, out var node))
        {
            return fallback;
        }

        if (node is null)
        {
            return null;
        }

        if (node is not JsonArray array)
        {
            throw new McpToolException("参数 " + name + " 必须是整数数组");
        }

        var result = new List<int>();
        foreach (var item in array)
        {
            if (item is not JsonValue value || !value.TryGetValue<int>(out var id) || id is < 1 or > 5)
            {
                throw new McpToolException("内容描述词 id 必须是 1-5");
            }

            if (!result.Contains(id))
            {
                result.Add(id);
            }
        }

        return result;
    }

    private static string VisibilityName(SteamWorkshopVisibility visibility) => visibility switch
    {
        SteamWorkshopVisibility.FriendsOnly => "friends",
        SteamWorkshopVisibility.Private => "private",
        SteamWorkshopVisibility.Unlisted => "unlisted",
        _ => "public",
    };

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static JsonObject Tool(string name, string description, JsonObject inputSchema) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = inputSchema,
    };

    private static JsonObject Schema(JsonObject properties, params string[] required)
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };

        if (required.Length > 0)
        {
            var array = new JsonArray();
            foreach (var name in required)
            {
                array.Add(name);
            }

            schema["required"] = array;
        }

        return schema;
    }

    private static JsonObject Property(string type, string description) => new()
    {
        ["type"] = type,
        ["description"] = description,
    };

    private static JsonArray StringArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(JsonValue.Create(value));
        }

        return array;
    }

    private static JsonArray IntArray(IEnumerable<int> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(JsonValue.Create(value));
        }

        return array;
    }

    /// <summary>工具层可预期的失败：作为 isError=true 的文本返回给 AI，而不是 JSON-RPC 错误。</summary>
    private sealed class McpToolException : Exception
    {
        public McpToolException(string message)
            : base(message)
        {
        }
    }

    private sealed record ModCandidate(ModLibraryEntry Entry, ModDescriptorInfo Descriptor);
}
