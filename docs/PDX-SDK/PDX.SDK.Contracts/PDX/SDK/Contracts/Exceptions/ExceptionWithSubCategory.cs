using System;

namespace PDX.SDK.Contracts.Exceptions;

public class ExceptionWithSubCategory : Exception
{
	public Enum Subcategory { get; private set; }

	public ExceptionWithSubCategory(Enum subcategory, string details)
		: base(details)
	{
		Subcategory = subcategory;
	}

	public Result ToResult()
	{
		return new Result
		{
			Success = false,
			Error = new Error(Subcategory, Message, "")
		};
	}
}
