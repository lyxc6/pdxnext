using System.Collections.Generic;
using PDX.SDK.Contracts.Configuration.ThirdParty;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Contracts.Util;

namespace PDX.SDK.Contracts.Configuration;

public class Config
{
	public BackendEnvironment? Environment { get; set; }

	public string GameVersion { get; set; }

	public string GameChecksum { get; set; }

	public Ecosystem? Ecosystem { get; set; }

	public string UserIdType { get; set; }

	public string UserId { get; set; }

	public LogLevel? LogLevel { get; set; }

	public ILogger Logger { get; set; }

	public string DiskIORoot { get; set; }

	public IDiskIO DiskIOHandler { get; set; }

	public int? ChunkSize { get; set; }

	public Language? Language { get; set; }

	public Dictionary<string, string> DefaultHeaders { get; set; }

	public ThirdPartyConfig ThirdParty { get; set; }

	public CloudSavesConfig CloudSaves { get; set; }

	public AchievementsConfig Achievements { get; set; }

	public ModsConfig Mods { get; set; }

	public bool? StandardTelemetryEnabled { get; set; }

	public bool? TelemetryDebugEnabled { get; set; }

	public bool? UnityEventTelemetryEnable { get; set; }

	public bool? SkipLegalChecksAndEnableTelemetry { get; set; }

	public Config()
	{
		ThirdParty = new ThirdPartyConfig();
		CloudSaves = new CloudSavesConfig();
		Achievements = new AchievementsConfig();
		Mods = new ModsConfig();
	}

	public void SetIfNotNull(Config cfg)
	{
		if (cfg.Environment.HasValue)
		{
			Environment = cfg.Environment;
		}
		if (cfg.GameVersion != null)
		{
			GameVersion = cfg.GameVersion;
		}
		if (cfg.GameChecksum != null)
		{
			GameChecksum = cfg.GameChecksum;
		}
		if (cfg.Ecosystem.HasValue)
		{
			Ecosystem = cfg.Ecosystem;
		}
		if (cfg.UserIdType != null)
		{
			UserIdType = cfg.UserIdType;
		}
		if (cfg.UserId != null)
		{
			UserId = cfg.UserId;
		}
		if (cfg.LogLevel.HasValue)
		{
			LogLevel = cfg.LogLevel;
		}
		if (cfg.Logger != null)
		{
			Logger = cfg.Logger;
		}
		if (cfg.DiskIORoot != null)
		{
			DiskIORoot = cfg.DiskIORoot;
		}
		if (cfg.DiskIOHandler != null)
		{
			DiskIOHandler = cfg.DiskIOHandler;
		}
		if (cfg.ChunkSize.HasValue)
		{
			ChunkSize = cfg.ChunkSize;
		}
		if (cfg.Language.HasValue)
		{
			Language = cfg.Language;
		}
		if (cfg.DefaultHeaders != null)
		{
			DefaultHeaders = cfg.DefaultHeaders;
		}
		if (cfg.StandardTelemetryEnabled.HasValue)
		{
			StandardTelemetryEnabled = cfg.StandardTelemetryEnabled;
		}
		if (cfg.TelemetryDebugEnabled.HasValue)
		{
			TelemetryDebugEnabled = cfg.TelemetryDebugEnabled;
		}
		if (cfg.UnityEventTelemetryEnable.HasValue)
		{
			UnityEventTelemetryEnable = cfg.UnityEventTelemetryEnable;
		}
		if (cfg.SkipLegalChecksAndEnableTelemetry.HasValue)
		{
			SkipLegalChecksAndEnableTelemetry = cfg.SkipLegalChecksAndEnableTelemetry;
		}
		if (cfg.ThirdParty != null)
		{
			ThirdParty.SetIfNotNull(cfg.ThirdParty);
		}
		if (cfg.CloudSaves != null)
		{
			CloudSaves.SetIfNotNull(cfg.CloudSaves);
		}
		if (cfg.Achievements != null)
		{
			Achievements.SetIfNotNull(cfg.Achievements);
		}
		if (cfg.Mods != null)
		{
			Mods.SetIfNotNull(cfg.Mods);
		}
	}

	public void SetDefaultConfigValues()
	{
		if (!LogLevel.HasValue)
		{
			LogLevel = PDX.SDK.Contracts.Enums.LogLevel.L3_Error;
		}
		if (!Environment.HasValue)
		{
			Environment = BackendEnvironment.Sandbox;
		}
		if (!Language.HasValue)
		{
			Language = PDX.SDK.Contracts.Enums.Language.en;
		}
		if (!Ecosystem.HasValue)
		{
			Ecosystem = PDX.SDK.Contracts.Enums.Ecosystem.Unidentified;
		}
		if (!StandardTelemetryEnabled.HasValue)
		{
			StandardTelemetryEnabled = true;
		}
		if (!TelemetryDebugEnabled.HasValue)
		{
			TelemetryDebugEnabled = false;
		}
		if (!UnityEventTelemetryEnable.HasValue)
		{
			UnityEventTelemetryEnable = false;
		}
		if (!ChunkSize.HasValue)
		{
			ChunkSize = 32768;
		}
		if (!SkipLegalChecksAndEnableTelemetry.HasValue)
		{
			SkipLegalChecksAndEnableTelemetry = false;
		}
		if (!ThirdParty.Xbox.Enabled.HasValue)
		{
			ThirdParty.Xbox.Enabled = false;
		}
		if (!ThirdParty.Psn.Enabled.HasValue)
		{
			ThirdParty.Psn.Enabled = false;
		}
		if (!ThirdParty.Psn.DoNpInit.HasValue)
		{
			ThirdParty.Psn.DoNpInit = true;
		}
		if (!ThirdParty.Nintendo.Enabled.HasValue)
		{
			ThirdParty.Nintendo.Enabled = false;
		}
		if (!ThirdParty.Apple.Enabled.HasValue)
		{
			ThirdParty.Apple.Enabled = false;
		}
		if (!ThirdParty.Google.Enabled.HasValue)
		{
			ThirdParty.Google.Enabled = false;
		}
		if (!ThirdParty.Google.SilentSignInOnly.HasValue)
		{
			ThirdParty.Google.SilentSignInOnly = false;
		}
		if (!ThirdParty.Stadia.Enabled.HasValue)
		{
			ThirdParty.Stadia.Enabled = false;
		}
		if (!ThirdParty.Steam.Enabled.HasValue)
		{
			ThirdParty.Steam.Enabled = false;
		}
		if (!ThirdParty.AutoLogin.HasValue)
		{
			ThirdParty.AutoLogin = false;
		}
		if (!ThirdParty.CheckForThirdPartyAutoLoginPrompt.HasValue)
		{
			ThirdParty.CheckForThirdPartyAutoLoginPrompt = true;
		}
	}
}
