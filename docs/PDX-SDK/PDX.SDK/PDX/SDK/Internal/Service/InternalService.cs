using System;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.StandardConstantValues;
using PDX.SDK.Internal.Util;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service;

[Preserve]
internal abstract class InternalService
{
	protected Context _context;

	protected string ServiceName;

	[Preserve]
	protected InternalService(string name, Context context)
	{
		ServiceName = name;
		_context = context;
	}

	protected void Log(string msg, LogLevel logLevel = LogLevel.L1_Debug, FlowData flowData = null)
	{
		if (_context != null)
		{
			_context.Log(msg, logLevel, flowData);
		}
	}

	protected Result NotLoggedIn(string action)
	{
		return NotLoggedIn<Result>(action);
	}

	protected T NotLoggedIn<T>(string action) where T : Result
	{
		return new Result
		{
			Success = false,
			Error = new Error(InvalidState.NotLoggedIn, string.Format(Defaults.Culture, "In order to {0}, you must first be logged in", action), "")
		}.CastTo<T>();
	}

	protected DateTime? ParseDateTime(string input, string functionName, string timeDescriptor, FlowData fd = null)
	{
		return input.ParseDateTime(functionName, timeDescriptor, ServiceName, _context.Logger, fd);
	}

	protected PDXResponseParser CreateResponseParser(Response response, FlowData flowData = null, bool hasJSONBody = true, bool strictHttpCode = false)
	{
		if (!strictHttpCode)
		{
			return new PDXResponseParser(_context, response, hasJSONBody, flowData);
		}
		return new StrictHttpCodePDXResponseParser(_context, response, hasJSONBody, flowData);
	}

	protected PDXResponseParser<T> CreateResponseParser<T>(Response response, FlowData flowData = null, bool hasJSONBody = true, bool strictHttpCode = false) where T : HTTPBodyBase
	{
		if (!strictHttpCode)
		{
			return new PDXResponseParser<T>(_context, response, hasJSONBody, flowData);
		}
		return new StrictHttpCodePDXResponseParser<T>(_context, response, hasJSONBody, flowData);
	}

	protected RequestBuilder CreateRequestBuilder(FlowData fd)
	{
		return new RequestBuilder(_context, fd);
	}
}
