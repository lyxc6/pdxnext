using System;
using System.Collections.Generic;
using PDX.SDK.Contracts.Enums.Errors;

namespace PDX.SDK.Internal.Mapper;

internal class OldErrorToNewErrorMapper
{
	private static readonly List<string> WildCardTransform = new List<string> { "found no account connected to", "Document ", "Invalid language, must be ISO 639-1:", "missing field", "Invalid name:" };

	private static Dictionary<Tuple<string, string>, Enum> TransformMap = new Dictionary<Tuple<string, string>, Enum>
	{
		{
			new Tuple<string, string>("not-authorized", ""),
			NotAuthorized.ForbiddenAccess
		},
		{
			new Tuple<string, string>("not-authorized", "Not authorized"),
			NotAuthorized.ForbiddenAccess
		},
		{
			new Tuple<string, string>("not-authorized", "Unauthorized"),
			NotAuthorized.ForbiddenAccess
		},
		{
			new Tuple<string, string>("not-authorized", "Expired session token."),
			NotAuthorized.ExpiredSessionToken
		},
		{
			new Tuple<string, string>("not-authorized", "Expired refresh token."),
			NotAuthorized.ExpiredRefreshToken
		},
		{
			new Tuple<string, string>("not-authorized", "No existing account"),
			NotAuthorized.ForbiddenAccess
		},
		{
			new Tuple<string, string>("not-authorized", "found no account connected to*"),
			NotAuthorized.ForbiddenAccess
		},
		{
			new Tuple<string, string>("not-authorized", "User is not authorized to access this resource"),
			NotAuthorized.ForbiddenAccess
		},
		{
			new Tuple<string, string>("not-authorized", "A user is not authorized to manage given activation set."),
			NotFound.NoModPlayset
		},
		{
			new Tuple<string, string>("forbidden-task-query", "Forbidden task query"),
			NotAuthorized.ForbiddenAccess
		},
		{
			new Tuple<string, string>("bad-password-length", ""),
			InvalidInput.InvalidLength
		},
		{
			new Tuple<string, string>("parse-failure", ""),
			InvalidInput.InvalidFormat
		},
		{
			new Tuple<string, string>("invalid-email", ""),
			BadUserInput.InvalidEmail
		},
		{
			new Tuple<string, string>("not-found", "Could not find document"),
			NotFound.NoDocument
		},
		{
			new Tuple<string, string>("not-found", "No matching document"),
			NotFound.NoDocument
		},
		{
			new Tuple<string, string>("not-found", "resource could not be found"),
			NotFound.NoResource
		},
		{
			new Tuple<string, string>("account-exists", ""),
			DuplicateEntry.AccountExists
		},
		{
			new Tuple<string, string>("dynamodb-failure", ""),
			InternalError.DBError
		},
		{
			new Tuple<string, string>("unsupported-account-details-keys", ""),
			InvalidInput.InvalidFormat
		},
		{
			new Tuple<string, string>("missing-required-account-details-keys", ""),
			InvalidInput.MissingParameter
		},
		{
			new Tuple<string, string>("invalid-country-code", ""),
			InvalidInput.InvalidFormat
		},
		{
			new Tuple<string, string>("invalid-language-code", ""),
			InvalidInput.InvalidFormat
		},
		{
			new Tuple<string, string>("invalid-script-code", ""),
			InvalidInput.InvalidFormat
		},
		{
			new Tuple<string, string>("invalid-ISO-8601-format", ""),
			InvalidInput.InvalidFormat
		},
		{
			new Tuple<string, string>("internal-error", "Provided rating value is not allowed."),
			InvalidInput.InvalidFormat
		},
		{
			new Tuple<string, string>("invalid-input-length", ""),
			InvalidInput.InvalidLength
		},
		{
			new Tuple<string, string>("ua", ""),
			BadUserInput.UA
		},
		{
			new Tuple<string, string>("third-party-exception", ""),
			InternalError.ThirdPartyException
		},
		{
			new Tuple<string, string>("duplicate-entry", "Document *"),
			DuplicateEntry.DocumentExists
		},
		{
			new Tuple<string, string>("session-does-not-exist", "there was no session found for the supplied input"),
			NotAuthorized.ExpiredSessionToken
		},
		{
			new Tuple<string, string>("internal-error", ""),
			InternalError.InternalError
		},
		{
			new Tuple<string, string>("bad-input", "Authorization header is not found"),
			InvalidInput.AuthMissing
		},
		{
			new Tuple<string, string>("bad-input", "Could not parse Authorization header => something wrong with session header"),
			InvalidInput.AuthMissing
		},
		{
			new Tuple<string, string>("bad-input", "Could not parse credentials from body"),
			InvalidInput.AuthMissing
		},
		{
			new Tuple<string, string>("bad-input", "Invalid language, must be ISO 639-1:*"),
			InvalidInput.InvalidFormat
		},
		{
			new Tuple<string, string>("bad-input", "Missing required query parameters"),
			InvalidInput.MissingParameter
		},
		{
			new Tuple<string, string>("bad-input", "missing field*"),
			InvalidInput.AuthMissing
		},
		{
			new Tuple<string, string>("bad-input", "Missing document type"),
			InvalidInput.MissingParameter
		},
		{
			new Tuple<string, string>("bad-input", "Invalid name:*"),
			InvalidInput.InvalidFormat
		},
		{
			new Tuple<string, string>("bad-input", "Invalid header"),
			InvalidInput.InvalidHeader
		},
		{
			new Tuple<string, string>("bad-input", "User already registered"),
			DuplicateEntry.AccountExists
		},
		{
			new Tuple<string, string>("bad-input", "Missing email"),
			InvalidInput.MissingParameter
		},
		{
			new Tuple<string, string>("bad-input", "Email is not valid"),
			BadUserInput.InvalidEmail
		},
		{
			new Tuple<string, string>("bad-input", "Missing password"),
			InvalidInput.MissingParameter
		},
		{
			new Tuple<string, string>("bad-input", "Specified mod could not be found."),
			NotFound.NoMod
		},
		{
			new Tuple<string, string>("bad-input", "Some of the provided mods do not exist"),
			NotFound.NoMod
		},
		{
			new Tuple<string, string>("bad-input", "This mod can not be installed"),
			NotFound.NoModVersion
		},
		{
			new Tuple<string, string>("bad-input", "Attribute fileName.fileExtension is invalid. It should be *"),
			InvalidInput.InvalidType
		},
		{
			new Tuple<string, string>("bad-input", "Mod content is missing."),
			InvalidInput.MissingParameter
		},
		{
			new Tuple<string, string>("profile-does-not-exists", "profile username does not exist in table"),
			NotFound.NoProfile
		},
		{
			new Tuple<string, string>("invalid-mod-state", "Updates are disabled for this mod."),
			InvalidState.ModDisabled
		},
		{
			new Tuple<string, string>("bad-input", "mods: Must be set"),
			InvalidInput.MissingParameter
		},
		{
			new Tuple<string, string>("bad-input", "activationSetId: Must be a positive integer"),
			InvalidInput.InvalidFormat
		}
	};

	public static Enum Map(string errorCode, string errorMessage)
	{
		if (errorMessage != null)
		{
			foreach (string item in WildCardTransform)
			{
				if (errorMessage.StartsWith(item))
				{
					errorMessage = item + "*";
					break;
				}
			}
		}
		Tuple<string, string> key = new Tuple<string, string>(errorCode, errorMessage);
		if (TransformMap.ContainsKey(key))
		{
			return TransformMap[key];
		}
		key = new Tuple<string, string>(errorCode, "");
		if (TransformMap.ContainsKey(key))
		{
			return TransformMap[key];
		}
		return BaseCategory.Undefined;
	}
}
