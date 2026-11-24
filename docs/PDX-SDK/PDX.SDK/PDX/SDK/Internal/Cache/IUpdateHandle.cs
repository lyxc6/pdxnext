using System;
using System.Threading.Tasks;
using PDX.SDK.Internal.Cache.Models;

namespace PDX.SDK.Internal.Cache;

internal interface IUpdateHandle<TCache>
{
	Task<UpdateResult<T>> Update<T>(Action<TCache> stateMutationHook) where T : class;
}
