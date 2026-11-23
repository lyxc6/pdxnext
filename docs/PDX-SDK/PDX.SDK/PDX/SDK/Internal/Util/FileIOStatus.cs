using PDX.SDK.Internal.Enums;

namespace PDX.SDK.Internal.Util;

internal class FileIOStatus
{
	public bool Success { get; set; }

	public DiskIOError ErrorCode { get; set; }

	public string Message { get; set; }
}
