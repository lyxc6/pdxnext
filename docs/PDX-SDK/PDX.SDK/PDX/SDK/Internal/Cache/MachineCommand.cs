using System.Threading.Tasks;
using PDX.SDK.Internal.Cache.Models;

namespace PDX.SDK.Internal.Cache;

internal class MachineCommand : IMachineCommand
{
	private readonly IUpdateHandle<CachedData> _updateHandle;

	public MachineCommand(IUpdateHandle<CachedData> updateHandle)
	{
		_updateHandle = updateHandle;
	}

	public Task<UpdateResult<IMachineData>> AddViewedDocument(string title)
	{
		return _updateHandle.Update<IMachineData>(delegate(CachedData state)
		{
			state.ViewedDocuments.Add(title);
		});
	}
}
