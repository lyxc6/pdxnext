using System;

namespace PDX.SDK.Contracts.Exceptions;

public class PlatypatchException : Exception
{
	public PlatypatchException(string message)
		: base(message)
	{
	}

	public PlatypatchException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
