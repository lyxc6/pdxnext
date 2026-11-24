using System;
using System.Text;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Internal.Mapper;
using PDX.SDK.Network.Models;
using TinyJson;

namespace PDX.SDK.Internal.Util;

internal class PDXResponseParser<T> where T : HTTPBodyBase
{
	private bool _hasJSONBody = true;

	private bool _malformedJSON;

	protected Response _httpResponse;

	private T _responseBody;

	private Context _context;

	private FlowData _flowData;

	internal PDXResponseParser(Context context, Response response, bool hasJSONBody = true, FlowData fd = null)
	{
		_context = context;
		_flowData = fd;
		_hasJSONBody = hasJSONBody;
		_httpResponse = response;
		if (_httpResponse == null)
		{
			return;
		}
		try
		{
			_malformedJSON = false;
			byte[] body = _httpResponse.Body;
			if (body == null)
			{
				_responseBody = null;
				return;
			}
			string text = Encoding.UTF8.GetString(body);
			if (string.IsNullOrWhiteSpace(text))
			{
				_responseBody = null;
			}
			else if (hasJSONBody)
			{
				_responseBody = text.FromJson<T>();
			}
		}
		catch (NullReferenceException)
		{
			_responseBody = null;
			_malformedJSON = true;
			Log("Could not parse HTTP response to default Result", LogLevel.L3_Error);
		}
		catch (IndexOutOfRangeException)
		{
			_responseBody = null;
			_malformedJSON = true;
			Log("Possible malformed JSON", LogLevel.L3_Error);
		}
	}

	private void Log(string msg, LogLevel logLevel = LogLevel.L1_Debug)
	{
		_context.Log(msg, logLevel, _flowData);
	}

	public virtual bool IsSuccess()
	{
		if (_httpResponse == null)
		{
			return false;
		}
		if (_httpResponse.StatusCode < 200 || _httpResponse.StatusCode > 299)
		{
			return false;
		}
		if (_responseBody == null)
		{
			if (_httpResponse.StatusCode == 204 || _httpResponse.StatusCode == 205)
			{
				return true;
			}
			if (_hasJSONBody)
			{
				return false;
			}
		}
		if (_hasJSONBody && (_responseBody.result == null || _responseBody.result != "OK"))
		{
			return false;
		}
		return true;
	}

	public T GetResponse()
	{
		return _responseBody;
	}

	public Result GetResultObject()
	{
		return GetResultObject<Result>();
	}

	public U GetResultObject<U>() where U : Result
	{
		U val = new U
		{
			Success = (!_malformedJSON && IsSuccess())
		};
		if (_responseBody != null && _responseBody.requestId != null)
		{
			val.RequestId = _responseBody.requestId;
		}
		if (!val.Success)
		{
			Error error = null;
			string bodyString = ((_httpResponse?.Body != null) ? Encoding.UTF8.GetString(_httpResponse.Body) : null);
			error = ((_responseBody == null || _responseBody.message != null) ? CreateDefaultErrors(bodyString) : ((_responseBody.error != null) ? ParseV2Error(bodyString) : ParseOldError(bodyString)));
			val.Error = MapThirdPartyError(error);
		}
		return val;
	}

	private static Error MapThirdPartyError(Error error)
	{
		if (error.Category != BaseCategory.NotAuthorized || !error.SubCategory.Equals(NotAuthorized.RejectedByThirdParty))
		{
			return error;
		}
		BaseCategory category = error.Category;
		Enum subCategory = error.SubCategory;
		return new Error(category.ToString(), subCategory.ToString(), error.Details, error.Raw);
	}

	private Error CreateDefaultErrors(string bodyString)
	{
		if (string.IsNullOrEmpty(bodyString))
		{
			return new Error(InvalidNetworkResponse.NoResponseBody, "", bodyString);
		}
		if (_malformedJSON)
		{
			return new Error(InvalidNetworkResponse.MalformedJSON, "", bodyString);
		}
		return new Error(bodyString);
	}

	private Error ParseOldError(string bodyString)
	{
		return new Error(OldErrorToNewErrorMapper.Map(_responseBody.errorCode, _responseBody.errorMessage), _responseBody.details ?? _responseBody.errorMessage, bodyString);
	}

	private Error ParseV2Error(string bodyString)
	{
		string categoryString = _responseBody.error.category.Replace("-", "");
		string subCategoryString = _responseBody.error.subCategory.Replace("-", "");
		return new Error(categoryString, subCategoryString, _responseBody.error.detail, bodyString);
	}
}
internal class PDXResponseParser : PDXResponseParser<HTTPBodyBase>
{
	internal PDXResponseParser(Context context, Response response, bool hasJSONBody = true, FlowData flowData = null)
		: base(context, response, hasJSONBody, flowData)
	{
	}
}
