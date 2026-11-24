using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using External.TinyIoc;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Configuration;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Events;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Contracts.Network;
using PDX.SDK.Contracts.Service.Account;
using PDX.SDK.Contracts.Service.Achievements;
using PDX.SDK.Contracts.Service.DataStorage;
using PDX.SDK.Contracts.Service.Generic;
using PDX.SDK.Contracts.Service.Inventory;
using PDX.SDK.Contracts.Service.Legal;
using PDX.SDK.Contracts.Service.Loyalty;
using PDX.SDK.Contracts.Service.Mods;
using PDX.SDK.Contracts.Service.Profile;
using PDX.SDK.Contracts.Service.Telemetry;
using PDX.SDK.Contracts.Service.ThirdParty;
using PDX.SDK.Contracts.StandardConstantValues;
using PDX.SDK.Internal.Cache;
using PDX.SDK.Internal.Credential;
using PDX.SDK.Internal.Enums;
using PDX.SDK.Internal.Events;
using PDX.SDK.Internal.Logging;
using PDX.SDK.Internal.Service.Account;
using PDX.SDK.Internal.Service.Achievements;
using PDX.SDK.Internal.Service.DataStorage;
using PDX.SDK.Internal.Service.DataStorage.CloudSave;
using PDX.SDK.Internal.Service.Inventory;
using PDX.SDK.Internal.Service.Legal;
using PDX.SDK.Internal.Service.Loyalty;
using PDX.SDK.Internal.Service.Mods;
using PDX.SDK.Internal.Service.Profile;
using PDX.SDK.Internal.Service.Telemetry;
using PDX.SDK.Internal.Service.ThirdParty;
using PDX.SDK.Internal.Util;
using PDX.SDK.Network;
using PDX.SDK.Network.DotNet;
using PDX.SDK.Util;

namespace PDX.SDK;

[Preserve]
public class Context : IContext
{
	private IModsService _modsService;

	private readonly Config _config;

	private const string SdkVersionTextFileName = "pdx_sdk_info.txt";

	public IAccountService Account { get; }

	public IAchievementsService Achievements { get; }

	public IDataStorageService DataStorage { get; }

	public IThirdPartyService ThirdParty { get; }

	public ILegalService Legal { get; }

	public ILoyaltyService Loyalty { get; }

	public IProfileService Profile { get; }

	public ITelemetryService Telemetry { get; }

	public IInventoryService Inventory { get; }

	public IModsService Mods => _modsService ?? (_modsService = Container.Resolve<IModsService>());

	public IEventHub Events { get; }

	public string Namespace { get; }

	public Platform Platform { get; }

	public Config Config => _config;

	public DateTime SessionStart { get; }

	internal SynchronizationContext MainThreadSynchronizationContext { get; private set; }

	internal HTTP HTTP { get; set; }

	internal FileIO FileIO { get; set; }

	internal StateCache StateCache { get; }

	internal ILogger Logger { get; private set; }

	internal string TelemetryUserIdType { get; set; }

	internal string PseudonymizedId { get; set; }

	internal string ApplicationSessionId { get; set; }

	internal string MachineUId { get; private set; }

	internal string MachineGeneratedUId { get; private set; }

	internal static FileLocks DiskFileLocks { get; private set; } = new FileLocks();

	internal static object StandardCallsLock { get; private set; } = new object();

	internal ICredentialFactory CredentialFactory { get; set; }

	internal IRequestBuilderCredentialBridge RequestBuilderCredentialBridge { get; set; }

	internal IPathFactory PathFactory { get; }

	internal ICancellationTokenRegister CancellationTokenRegister { get; }

	internal TinyIoCContainer Container { get; }

	public static async Task<IContext> Create(Platform platform, string @namespace, Config config = null)
	{
		Context context = CreateWithoutInit(platform, @namespace, config);
		await context.Init().ConfigureAwait(continueOnCapturedContext: false);
		return context;
	}

	internal static Context CreateWithoutInit(Platform platform, string @namespace, Config config = null)
	{
		config = config ?? new Config();
		ValidateConfig(config);
		return new Context(platform, @namespace, config);
	}

	private static void ValidateConfig(Config config)
	{
	}

	internal async Task<bool> GetLoggedIn()
	{
		return (await StateCache.GetPlayerData().ConfigureAwait(continueOnCapturedContext: false)).LoggedIn;
	}

	private Context(Platform platform, string @namespace, Config config)
	{
		Namespace = @namespace;
		SessionStart = DateTime.UtcNow;
		ApplicationSessionId = GenerateUUId();
		Platform = platform;
		_config = config ?? new Config();
		_config.SetDefaultConfigValues();
		Logger = _config.Logger ?? new Logger(_config.LogLevel);
		HTTP = CreateHttpClient();
		PathFactory = new PathFactory(this);
		FileIO = new FileIO(this, PathFactory);
		CancellationTokenRegister = new CancellationTokenRegister(Logger);
		if (Config.DiskIOHandler != null)
		{
			OnDiskIOUpdated();
		}
		StateCache = new StateCache(FileIO, Logger);
		Events = new InternalEventHub(MainThreadSynchronizationContext);
		Container = new TinyIoCContainer();
		Container.RegisterSdkDependencies(this);
		Container.RegisterSdkServices(Namespace);
		Account = new AccountService(this);
		Achievements = ConstructAchievementsService();
		DataStorage = new DataStorageService(this, PathFactory);
		ThirdParty = new ThirdPartyService(this);
		Inventory = new InventoryService(this);
		Legal = new LegalService(this);
		Loyalty = new LoyaltyService(this);
		Profile = new ProfileService(this);
		Telemetry = ConstructTelemetryService();
		CredentialFactory = new CredentialFactory(this);
		RequestBuilderCredentialBridge = new RequestBuilderCredentialBridge();
	}

	private TelemetryService ConstructTelemetryService()
	{
		IPseudonymizationService pseudonymizationService = new PseudonymizationService(this);
		return new TelemetryService(this, pseudonymizationService);
	}

	public async Task UpdateConfig(Config cfg)
	{
		Config.SetIfNotNull(cfg);
		await OnConfigUpdated(cfg).ConfigureAwait(continueOnCapturedContext: false);
	}

	public void Log(string msg, LogLevel logLevel = LogLevel.L1_Debug, FlowData flowData = null)
	{
		Logger.Log(msg, logLevel, flowData);
	}

	public async Task<Result> Shutdown()
	{
		if (Config.StandardTelemetryEnabled == true)
		{
			await Telemetry.Exit().ConfigureAwait(continueOnCapturedContext: false);
		}
		CancellationTokenRegister.CancelAllTokens();
		return new Result
		{
			Success = true
		};
	}

	public List<ITransferStatus> GetTransfers(ETransferType? transferType = null, ETransferState? state = null, ETransferServiceType? serviceType = null, string transferId = null)
	{
		List<ITransferStatus> transfers = HTTP.GetTransfers();
		if (!transferType.HasValue && !state.HasValue && !serviceType.HasValue && transferId == null)
		{
			return transfers;
		}
		List<ITransferStatus> list = new List<ITransferStatus>();
		foreach (ITransferStatus item in transfers)
		{
			if ((!transferType.HasValue || transferType.Value == item.Type) && (!state.HasValue || item.State == state.Value) && (!serviceType.HasValue || item.ServiceType == serviceType) && (transferId == null || !(item.Id != transferId)))
			{
				list.Add(item);
			}
		}
		return list;
	}

	public IStorageInformation GetStorageInformation()
	{
		return ThirdPartyHelper.GetDeviceStorageInformation(PathFactory.GetPath(""), Logger);
	}

	internal async Task Init()
	{
		await MainThreadSynchronizationContext.OnMainThread(delegate
		{
			PathFactory.Configure(Config);
		}).ConfigureAwait(continueOnCapturedContext: false);
		if (Platform == Platform.Windows || Platform == Platform.MacOS || Platform == Platform.Linux)
		{
			await WriteSDKInfoFile().ConfigureAwait(continueOnCapturedContext: false);
		}
		await StateCache.Init().ConfigureAwait(continueOnCapturedContext: false);
		await ((Mods as ModsService).ModsCache as ModsCache).Init().ConfigureAwait(continueOnCapturedContext: false);
		await (Achievements as AchievementsService).AchievementsDataCache.Init().ConfigureAwait(continueOnCapturedContext: false);
		await (Achievements as AchievementsService).AchievementsImageCache.Init().ConfigureAwait(continueOnCapturedContext: false);
		DoPlatformSpecificInit();
		await SetTelemetryUUId().ConfigureAwait(continueOnCapturedContext: false);
		await SetMachineUid().ConfigureAwait(continueOnCapturedContext: false);
		CleanTempFileStorage();
		OnDefaultHeadersUpdated();
		if (Config.SkipLegalChecksAndEnableTelemetry == true && Telemetry is TelemetryService)
		{
			TelemetryService telemetryInternal = Telemetry as TelemetryService;
			await telemetryInternal.SetAgeCheckStatus(isUserNotUnderAge: true).ConfigureAwait(continueOnCapturedContext: false);
			await telemetryInternal.SetAllLegalDocumentsViewedDone().ConfigureAwait(continueOnCapturedContext: false);
		}
		if (Config.StandardTelemetryEnabled == true)
		{
			await Telemetry.Start().ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	internal string GetPlatform()
	{
		return Platform.GetPlatform();
	}

	internal async Task SetLoggedIn(string userId, string sessionToken, string refreshToken, TrustLevel trustLevel, string @namespace, FlowData flowData = null)
	{
		Log(string.Format(Defaults.Culture, "Account logged in.\nUserId: {0}\nSessionToken: {1}\nRefreshToken: {2}", userId, sessionToken, refreshToken), LogLevel.L1_Debug, flowData);
		await StateCache.Player.SetLoggedIn(userId).ConfigureAwait(continueOnCapturedContext: false);
		await StateCache.Session.SetSession(sessionToken, refreshToken, trustLevel, @namespace).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task ClearLoginSession(bool hardClear = false)
	{
		await StateCache.Player.SetLoggedOut(hardClear).ConfigureAwait(continueOnCapturedContext: false);
		if (hardClear)
		{
			await StateCache.Session.ClearSession().ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	internal async Task SetTelemetryData(string pseudonymizedId, string idType)
	{
		PseudonymizedId = pseudonymizedId;
		TelemetryUserIdType = idType;
		await StateCache.Player.SetPseudonymizedUserId(pseudonymizedId, idType).ConfigureAwait(continueOnCapturedContext: false);
	}

	private HTTP CreateHttpClient()
	{
		return new HTTPDotNet(Logger);
	}

	private IAchievementsService ConstructAchievementsService()
	{
		AchievementsMetadataCache achievementsDataCache = new AchievementsMetadataCache(FileIO, Logger);
		AchievementsImageCache achievementsImageCache = new AchievementsImageCache(FileIO, Logger);
		return new AchievementsService(this, achievementsDataCache, achievementsImageCache);
	}

	private async Task OnConfigUpdated(Config cfg)
	{
		if (cfg.DefaultHeaders != null)
		{
			OnDefaultHeadersUpdated();
		}
		if (cfg.DiskIOHandler != null)
		{
			OnDiskIOUpdated();
		}
		if (cfg.LogLevel.HasValue)
		{
			Logger = new Logger(cfg.LogLevel);
		}
		await PathFactory.Configure(cfg).ConfigureAwait(continueOnCapturedContext: false);
	}

	private void OnDiskIOUpdated()
	{
		if (Config.DiskIOHandler != null)
		{
			FileIO.DiskIO = Config.DiskIOHandler;
		}
	}

	private void OnDefaultHeadersUpdated()
	{
		HTTP.ClearDefaultHeaders();
		if (Config.DefaultHeaders != null)
		{
			foreach (KeyValuePair<string, string> defaultHeader in Config.DefaultHeaders)
			{
				HTTP.AddDefaultHeader(defaultHeader.Key, defaultHeader.Value);
			}
		}
		HTTP.AddDefaultHeader("X-PDX-Error-Version", "v2");
		HTTP.SetPDXHeaders(Namespace, _config.GameVersion, SDKVersion.GetVersion(), Platform.GetPlatform());
	}

	private async Task<bool> SetMachineUid()
	{
		return await MainThreadSynchronizationContext.OnMainThread(() => Task.FromResult(InnerSetMachineUid())).ConfigureAwait(continueOnCapturedContext: false);
	}

	private bool InnerSetMachineUid()
	{
		MachineUId = null;
		return false;
	}

	private async Task SetTelemetryUUId()
	{
		OperationResult<string> operationResult = await FileIO.ReadText(FilePath.SessionId).ConfigureAwait(continueOnCapturedContext: false);
		if (operationResult.Status.Success && operationResult.Result != null)
		{
			MachineGeneratedUId = operationResult.Result;
			return;
		}
		MachineGeneratedUId = GenerateUUId();
		await FileIO.Write(FilePath.SessionId, MachineGeneratedUId).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task WriteSDKInfoFile()
	{
		string path = PathFactory.GetPath("pdx_sdk_info.txt");
		string s = new StringBuilder(64).Append(SDKVersion.GetVersion()).Append("|").Append(SDKVersion.GetRevisionVersion())
			.Append("|")
			.Append(SDKVersion.GetBuildTime())
			.ToString();
		MemoryStream data = new MemoryStream(Encoding.UTF8.GetBytes(s));
		await FileIO.Write(path, data).ConfigureAwait(continueOnCapturedContext: false);
	}

	private string GenerateUUId()
	{
		return BitConverter.ToString(Guid.NewGuid().ToByteArray()).Replace("-", "");
	}

	internal async Task CleanUpSyncedState()
	{
		await (Mods as ModsService).ModsCache.CleanUpSyncedModState(hardClean: true).ConfigureAwait(continueOnCapturedContext: false);
		await (DataStorage.CloudSave as CloudSaveService).CleanUpCloudSavesFromDisk().ConfigureAwait(continueOnCapturedContext: false);
	}

	private void DoPlatformSpecificInit()
	{
	}

	[Preserve]
	public static string[] GetMiscUtilStrings()
	{
		return new string[5] { "C# SDK Devs old and new:", "Mai-Khanh Lê", "Mikolaj Trzeciecki", "Niklas Hedlund", "Thomas Cronholm" };
	}

	internal void CleanTempFileStorage()
	{
		string path = PathFactory.GetPath("");
		string text = PathFactory.GetPatchDownloadStagingPath().Split(Path.DirectorySeparatorChar).Last();
		Log(string.Format(Defaults.Culture, "rootPath {0} tempSubdirectory {1}", path, text));
		foreach (string item in FileIO.DiskIO.FindSubdirectoriesRecursive(path, text))
		{
			FileIO.DiskIO.DeleteDirectory(item, recursive: true);
		}
	}
}
