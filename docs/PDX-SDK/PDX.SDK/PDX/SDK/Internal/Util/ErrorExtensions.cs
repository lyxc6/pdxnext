using System;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Internal.Enums;

namespace PDX.SDK.Internal.Util;

internal static class ErrorExtensions
{
	public static Enum ToInternalError(this DiskIOError error)
	{
		return error switch
		{
			DiskIOError.OutOfSpace => InternalError.DiskFullError, 
			DiskIOError.NotAllowed => InternalError.DiskPermissionError, 
			_ => InternalError.FileIOError, 
		};
	}
}
