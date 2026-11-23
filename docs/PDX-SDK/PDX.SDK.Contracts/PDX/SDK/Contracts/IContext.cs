using System.Collections.Generic;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Configuration;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Events;
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

namespace PDX.SDK.Contracts;

[Preserve]
public interface IContext
{
	IAccountService Account { get; }

	IAchievementsService Achievements { get; }

	Config Config { get; }

	IDataStorageService DataStorage { get; }

	IInventoryService Inventory { get; }

	ILegalService Legal { get; }

	ILoyaltyService Loyalty { get; }

	IModsService Mods { get; }

	string Namespace { get; }

	Platform Platform { get; }

	IProfileService Profile { get; }

	ITelemetryService Telemetry { get; }

	IThirdPartyService ThirdParty { get; }

	IEventHub Events { get; }

	List<ITransferStatus> GetTransfers(ETransferType? transferType = null, ETransferState? state = null, ETransferServiceType? serviceType = null, string transferId = null);

	IStorageInformation GetStorageInformation();

	Task<Result> Shutdown();

	Task UpdateConfig(Config cfg);
}
