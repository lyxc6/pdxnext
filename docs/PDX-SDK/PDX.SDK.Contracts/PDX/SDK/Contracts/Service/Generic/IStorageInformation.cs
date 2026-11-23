namespace PDX.SDK.Contracts.Service.Generic;

public interface IStorageInformation
{
	long AvailableFreeBytes { get; }

	long TotalBytes { get; }
}
