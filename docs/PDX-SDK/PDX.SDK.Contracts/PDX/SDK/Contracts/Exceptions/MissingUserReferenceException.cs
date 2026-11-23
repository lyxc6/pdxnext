using System;

namespace PDX.SDK.Contracts.Exceptions;

public class MissingUserReferenceException : Exception
{
	public MissingUserReferenceException(string message)
		: base(message)
	{
	}
}
