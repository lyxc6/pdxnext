using System.Threading.Tasks;
using PDX.SDK.Internal.Cache.Models;

namespace PDX.SDK.Internal.Cache;

internal interface IMachineCommand
{
	Task<UpdateResult<IMachineData>> AddViewedDocument(string title);
}
