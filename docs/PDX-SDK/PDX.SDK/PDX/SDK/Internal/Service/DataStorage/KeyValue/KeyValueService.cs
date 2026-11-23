using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.DataStorage.KeyValue;
using PDX.SDK.Contracts.Service.DataStorage.KeyValue.Models;
using PDX.SDK.Contracts.Service.DataStorage.KeyValue.Result;
using PDX.SDK.Internal.Service.DataStorage.KeyValue.Request;
using PDX.SDK.Internal.Service.DataStorage.KeyValue.Response;
using PDX.SDK.Internal.Util;
using PDX.SDK.Network.Enums;

namespace PDX.SDK.Internal.Service.DataStorage.KeyValue;

internal class KeyValueService : InternalService, IKeyValueService
{
	internal KeyValueService(Context context)
		: base("DataStorage.KeyValue", context)
	{
	}

	public async Task<GetResult> Get(string key)
	{
		return await Get(key, (FlowData)null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GetAllResult> Get(params string[] keys)
	{
		return await Get(keys, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GetAllResult> Get(IEnumerable<string> keys)
	{
		return await Get(keys, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GetAllResult> GetAll()
	{
		return await GetAll(null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Set(string key, string value)
	{
		return await Set(new List<Entry>
		{
			new Entry(key, value)
		}, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Set(Entry entry)
	{
		return await Set(new List<Entry> { entry }, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Set(IEnumerable<Entry> entries)
	{
		return await Set(entries, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Set(Dictionary<string, string> keyValuePairs)
	{
		List<Entry> list = new List<Entry>();
		foreach (KeyValuePair<string, string> keyValuePair in keyValuePairs)
		{
			list.Add(new Entry(keyValuePair.Key, keyValuePair.Value));
		}
		return await Set(list, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Delete(IEnumerable<Entry> entries)
	{
		return await Delete(entries, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Delete(Entry entry)
	{
		return await Delete(entry.Key, (FlowData)null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Delete(params string[] keys)
	{
		return await Delete(keys, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Delete(IEnumerable<string> keys)
	{
		return await Delete(keys, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Delete(string key)
	{
		return await Delete(key, (FlowData)null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> DeleteAll()
	{
		return await DeleteAll(null).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<GetAllResult> Get(IEnumerable<string> keys, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("DataStorage.KeyValue.Read", LogLevel.L0_Info, flowData2);
		List<Entry> entries = new List<Entry>();
		List<Task<GetResult>> list = new List<Task<GetResult>>();
		foreach (string key in keys)
		{
			list.Add(Get(key));
		}
		GetResult[] array = await Task.WhenAll(list.ToArray()).ConfigureAwait(continueOnCapturedContext: false);
		foreach (GetResult getResult in array)
		{
			if (!getResult.Success || getResult.Entry == null)
			{
				return getResult.CastTo<GetAllResult>();
			}
			entries.Add(getResult.Entry);
		}
		return new GetAllResult
		{
			Success = true,
			Entries = entries
		};
	}

	internal async Task<GetResult> Get(string key, FlowData flowData = null)
	{
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<GetResult>("retrieve a key value pair");
		}
		if (!Uri.IsWellFormedUriString(key, UriKind.Relative) || string.IsNullOrWhiteSpace(key))
		{
			return new Result
			{
				Success = false,
				Error = new Error(InvalidInput.InvalidFormat, $"Parameter 'key' is invalid format and should be in accordance with RFC 3986 and RFC 3987: \"{key}\"", "")
			}.CastTo<GetResult>();
		}
		FlowData fd = new FlowData(flowData);
		Log("DataStorage.KeyValue.Read", LogLevel.L0_Info, fd);
		PDXResponseParser<GetEntryResponse> pDXResponseParser = CreateResponseParser<GetEntryResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetPath("storage/u").AddNamespaceToPath()
			.AddToPath(key)
			.SetAuthHeader()
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		GetEntryResponse response = pDXResponseParser.GetResponse();
		GetResult resultObject = pDXResponseParser.GetResultObject<GetResult>();
		if (!resultObject.Success || response.entries == null || response.entries.Length == 0)
		{
			return resultObject;
		}
		EntryResponse entryResponse = response.entries[0];
		resultObject.Entry = new Entry(entryResponse.path, entryResponse.value, entryResponse.timestamp);
		return resultObject;
	}

	internal async Task<GetAllResult> GetAll(FlowData flowData = null)
	{
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<GetAllResult>("retrieve a key value pair");
		}
		FlowData fd = new FlowData(flowData);
		Log("DataStorage.KeyValue.Get", LogLevel.L0_Info, fd);
		PDXResponseParser<GetEntryResponse> pDXResponseParser = CreateResponseParser<GetEntryResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetPath("storage/u").AddNamespaceToPath()
			.SetAuthHeader()
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		GetEntryResponse response = pDXResponseParser.GetResponse();
		GetAllResult resultObject = pDXResponseParser.GetResultObject<GetAllResult>();
		if (!resultObject.Success || response.entries == null || response.entries.Length == 0)
		{
			return resultObject;
		}
		resultObject.Entries = new List<Entry>();
		EntryResponse[] entries = response.entries;
		foreach (EntryResponse entryResponse in entries)
		{
			Entry item = new Entry(entryResponse.path, entryResponse.value, entryResponse.timestamp);
			resultObject.Entries.Add(item);
		}
		return resultObject;
	}

	internal async Task<Result> Set(IEnumerable<Entry> entries, FlowData flowData = null)
	{
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<Result>("add a key value pair");
		}
		FlowData fd = new FlowData(flowData);
		Log("DataStorage.KeyValue.Set", LogLevel.L0_Info, fd);
		List<KeyValuePairsRequest> list = new List<KeyValuePairsRequest>();
		foreach (Entry entry in entries)
		{
			if (!Uri.IsWellFormedUriString(entry.Key, UriKind.Relative) || string.IsNullOrWhiteSpace(entry.Key))
			{
				return new Result
				{
					Success = false,
					Error = new Error(InvalidInput.InvalidFormat, $"Parameter 'key' is invalid format and should be in accordance with RFC 3986 and RFC 3987: \"{entry.Key}\"", "")
				};
			}
			list.Add(new KeyValuePairsRequest
			{
				path = entry.Key,
				value = entry.Value
			});
		}
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.PUT).SetPath("storage/u").AddNamespaceToPath()
			.SetAuthHeader()
			.SetBody(list)
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
	}

	internal async Task<Result> Delete(string key, FlowData flowData = null)
	{
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<Result>("delete a key value pair");
		}
		if (!Uri.IsWellFormedUriString(key, UriKind.Relative) || string.IsNullOrWhiteSpace(key))
		{
			return new Result
			{
				Success = false,
				Error = new Error(InvalidInput.InvalidFormat, $"Parameter 'key' is invalid format and should be in accordance with RFC 3986 and RFC 3987: \"{key}\"", "")
			};
		}
		FlowData fd = new FlowData(flowData);
		Log("DataStorage.KeyValue.Delete", LogLevel.L0_Info, fd);
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.DELETE).SetPath("storage/u").AddNamespaceToPath()
			.AddToPath(key)
			.SetAuthHeader()
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
	}

	internal async Task<Result> Delete(IEnumerable<string> keys, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("DataStorage.KeyValue.Delete", LogLevel.L0_Info, flowData2);
		return await keys.Select((string key) => Delete(key)).ToArray().GetResultFromTasks()
			.ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<Result> Delete(IEnumerable<Entry> entries, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("DataStorage.KeyValue.Delete", LogLevel.L0_Info, flowData2);
		return await entries.Select((Entry entry) => Delete(entry.Key)).ToArray().GetResultFromTasks()
			.ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<Result> DeleteAll(FlowData flowData = null)
	{
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<Result>("delete a key value pair");
		}
		FlowData fd = new FlowData(flowData);
		Log("DataStorage.KeyValue.DeleteAll", LogLevel.L0_Info, fd);
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.DELETE).SetPath("storage/u").AddNamespaceToPath()
			.SetAuthHeader()
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
	}
}
