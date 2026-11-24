using System;
using System.IO;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Configuration;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Service.Mods.Enums;

namespace PDX.SDK.Internal.Util;

internal class PathFactory : IPathFactory
{
	private const string ROOT_FOLDER_NAME = "Paradox";

	private const string CLOUD_SAVES_FOLDER_NAME = "cloudsaves";

	private const string ACHIEVEMENTS_FOLDER_NAME = "achievements";

	private const string MODS_FOLDER_NAME = "mods";

	private const string PLAYSETS_METADATA_FOLDER_NAME = "playsets_metadata";

	private const string SUBSCRIBED_FOLDER_NAME = "mods_subscribed";

	private const string WORK_IN_PROGRESS_FOLDER_NAME = "mods_workInProgress";

	private const string UNMANAGED_FOLDER_NAME = "mods_unmanaged";

	private const string CACHE_JOURNAL_FOLDER_NAME = "cache_journal";

	private const string ROOT_TEMP_FOLDER_NAME = "temp";

	private const string PATCH_DOWNLOAD_STAGING_TEMP = "patch_staging";

	private string _rootPath;

	private string _cloudSavesRootPath;

	private string _achievementsRootPath;

	private string _modsRootPath;

	private string _defaultRootPath;

	private readonly IContext _context;

	public PathFactory(IContext context)
	{
		_context = context;
	}

	private void DefineDefaultRootPath()
	{
		Platform platform = _context.Platform;
		if ((uint)platform <= 2u)
		{
			_defaultRootPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		}
		else
		{
			_defaultRootPath = "";
		}
		_defaultRootPath = (string.IsNullOrEmpty(_defaultRootPath) ? "Paradox" : Path.Combine(_defaultRootPath, "Paradox"));
		_defaultRootPath = Path.Combine(_defaultRootPath, _context.Namespace);
	}

	public Task Configure(Config config)
	{
		SetIfNotNull(config);
		if (string.IsNullOrEmpty(_rootPath))
		{
			_rootPath = _defaultRootPath;
		}
		return Task.CompletedTask;
	}

	private void SetIfNotNull(Config config)
	{
		if (string.IsNullOrEmpty(config.DiskIORoot))
		{
			DefineDefaultRootPath();
		}
		else
		{
			_rootPath = config.DiskIORoot;
		}
		if (!string.IsNullOrEmpty(config.CloudSaves.RootPath))
		{
			_cloudSavesRootPath = config.CloudSaves.RootPath;
		}
		if (!string.IsNullOrEmpty(config.Achievements.RootPath))
		{
			_achievementsRootPath = config.Achievements.RootPath;
		}
		if (!string.IsNullOrEmpty(config.Mods.RootPath))
		{
			_modsRootPath = config.Mods.RootPath;
		}
	}

	public string GetCloudSavesPath(string path = "")
	{
		if (string.IsNullOrEmpty(_cloudSavesRootPath))
		{
			return GetPathWithCurrentCacheRoot(Path.Combine("cloudsaves", path));
		}
		return Path.Combine(_cloudSavesRootPath, path);
	}

	public string GetAchievementsPath(string path = "")
	{
		if (string.IsNullOrEmpty(_achievementsRootPath))
		{
			return GetPathWithCurrentCacheRoot(Path.Combine("achievements", path));
		}
		return Path.Combine(_achievementsRootPath, path);
	}

	public string GetModsRootPath(string path = "")
	{
		if (string.IsNullOrEmpty(_modsRootPath))
		{
			return GetPathWithCurrentCacheRoot(Path.Combine("mods", path));
		}
		return Path.Combine(_modsRootPath, path);
	}

	public string GetModsPath(string path = "", LocalType type = LocalType.Subscribed)
	{
		switch (type)
		{
		case LocalType.Subscribed:
			path = Path.Combine("mods_subscribed", path);
			break;
		case LocalType.WorkInProgress:
			path = Path.Combine("mods_workInProgress", path);
			break;
		case LocalType.Unmanaged:
			path = Path.Combine("mods_unmanaged", path);
			break;
		}
		return GetModsRootPath(path);
	}

	public string GetModsMetadataPath(string path = "", LocalType type = LocalType.Subscribed)
	{
		return GetModsPath(Path.Combine(path, ".metadata", "metadata.json"), type);
	}

	public string GetModPlaysetsMetadataPath(string path = "")
	{
		return GetModsRootPath(Path.Combine("playsets_metadata", path));
	}

	public string GetJournalPath(string journalName)
	{
		return GetPathWithCurrentCacheRoot(Path.Combine("cache_journal", journalName));
	}

	public string GetPatchDownloadStagingPath(string path = "")
	{
		return GetPathWithCurrentCacheRoot(Path.Combine("temp", "patch_staging", path));
	}

	public string GetPath(string path)
	{
		return Path.Combine(_rootPath, path);
	}

	public string GetTempPath(string path = "")
	{
		return GetPathWithCurrentCacheRoot(Path.Combine("temp", path));
	}

	public string GetPathWithCurrentCacheRoot(string path = "")
	{
		return Path.Combine(_rootPath, path);
	}
}
