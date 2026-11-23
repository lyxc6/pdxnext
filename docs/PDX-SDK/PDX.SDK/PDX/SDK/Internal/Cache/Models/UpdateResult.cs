using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Cache.Models;

internal class UpdateResult<T>
{
	public FileIOStatus Status { get; set; }

	public T NewState { get; set; }
}
