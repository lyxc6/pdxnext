using PDX.SDK.Contracts.Service.Generic;

namespace PDX.SDK.Internal.Service.Generic;

internal class StorageInformation : IStorageInformation
{
	public long AvailableFreeBytes { get; set; }

	public long TotalBytes { get; set; }
}
