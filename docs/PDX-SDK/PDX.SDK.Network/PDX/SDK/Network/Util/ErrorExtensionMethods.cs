using System;
using PDX.SDK.Contracts;
using PDX.SDK.Network.Models;
using TinyJson;

namespace PDX.SDK.Network.Util;

public static class ErrorExtensionMethods
{
	public static string GenerateErrorBody(this Enum subCategory, string details)
	{
		Error error = new Error(subCategory, details, "");
		return new HTTPBodyBase
		{
			result = "Failure",
			error = new HTTPBodyBaseError
			{
				category = error.Category.ToString(),
				subCategory = error.SubCategory.ToString(),
				detail = details
			}
		}.ToJson();
	}
}
