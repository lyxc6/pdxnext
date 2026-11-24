using System.Threading.Tasks;

namespace PDX.SDK.Internal.Cache;

internal interface IStateCache
{
	ISessionCommand Session { get; }

	IPlayerCommand Player { get; }

	IMachineCommand Machine { get; }

	Task<ISessionData> GetSessionData();

	Task<IPlayerData> GetPlayerData();

	Task<IMachineData> GetMachineData();
}
