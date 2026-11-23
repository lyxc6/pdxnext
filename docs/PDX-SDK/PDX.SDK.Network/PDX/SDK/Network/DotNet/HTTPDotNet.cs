using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Contracts.Network;
using PDX.SDK.Contracts.StandardConstantValues;
using PDX.SDK.Contracts.Util;
using PDX.SDK.Network.Enums;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Network.DotNet;

public class HTTPDotNet : HTTP
{
	protected List<ITransferStatus> ongoingTransfers;

	protected static object ongoingTransfersLock = new object();

	private TimeSpan timeoutREST = new TimeSpan(0, 0, 15);

	private TimeSpan timeoutFileDownload = new TimeSpan(24, 0, 0);

	private TimeSpan timeoutFileUpload = new TimeSpan(24, 0, 0);

	private HttpClient client;

	private readonly ILogger _logger;

	public HTTPDotNet(ILogger logger)
	{
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Expected O, but got Unknown
		_logger = logger;
		client = new HttpClient();
		client.Timeout = Timeout.InfiniteTimeSpan;
		ServicePointManager.Expect100Continue = true;
		ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
		ongoingTransfers = new List<ITransferStatus>();
	}

	protected void Log(string msg, LogLevel logLevel = LogLevel.L0_Info, FlowData flowData = null)
	{
		if (_logger != null)
		{
			_logger.Log(msg, logLevel, flowData);
		}
		else
		{
			Console.WriteLine(msg);
		}
	}

	protected async Task<Response> ParseResponse(HttpResponseMessage msg, FlowData flowData)
	{
		Response retVal = new Response
		{
			StatusCode = (int)msg.StatusCode
		};
		Log("==Response Status Code==", LogLevel.L1_Debug, flowData);
		Log(retVal.StatusCode.ToString(Defaults.Culture), LogLevel.L1_Debug, flowData);
		Log("==Response Headers==", LogLevel.L0_Info, flowData);
		foreach (KeyValuePair<string, IEnumerable<string>> item in (HttpHeaders)msg.Headers)
		{
			string text = "";
			using (IEnumerator<string> enumerator2 = item.Value.GetEnumerator())
			{
				if (enumerator2.MoveNext())
				{
					text = enumerator2.Current;
				}
			}
			Log(string.Format(Defaults.Culture, "{0} : {1}", item.Key, text), LogLevel.L0_Info, flowData);
			retVal.Headers.Add(item.Key, text);
		}
		if (msg.Content != null)
		{
			Response response = retVal;
			response.Body = await msg.Content.ReadAsByteArrayAsync().ConfigureAwait(continueOnCapturedContext: false);
			Log("==Response Body==", LogLevel.L0_Info, flowData);
			Log(Encoding.UTF8.GetString(retVal.Body), LogLevel.L0_Info, flowData);
		}
		return retVal;
	}

	protected HttpRequestMessage PdxToNetRequest(string url, Request pdxRequest, HttpMethod restVerb, IChunkHandler uploadChunkHandler = null)
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Expected O, but got Unknown
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Expected O, but got Unknown
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Expected O, but got Unknown
		Action<string, LogLevel> action = delegate(string msg, LogLevel level)
		{
			Log(msg, level, pdxRequest?.FlowData);
		};
		HttpRequestMessage val = new HttpRequestMessage();
		val.Method = restVerb;
		val.RequestUri = new Uri(url);
		action("==Request URL==", LogLevel.L1_Debug);
		action(((object)restVerb).ToString() + " " + url, LogLevel.L1_Debug);
		if (pdxRequest == null)
		{
			return val;
		}
		if (uploadChunkHandler != null)
		{
			val.Content = (HttpContent)(object)new ProgressableStreamContent(uploadChunkHandler);
		}
		else if (pdxRequest.Body != null)
		{
			val.Content = (HttpContent)new ByteArrayContent(pdxRequest.Body);
		}
		bool flag = false;
		foreach (Header header in pdxRequest.Headers)
		{
			if (header.Key == "Content-Type")
			{
				if (val.Content != null)
				{
					val.Content.Headers.ContentType = new MediaTypeHeaderValue(header.Value);
					if (header.Value == "application/json")
					{
						flag = true;
					}
				}
			}
			else if (header.Key == "Authorization" || header.Key == "User-Agent" || header.Key == "If-None-Match")
			{
				((HttpHeaders)val.Headers).TryAddWithoutValidation(header.Key, header.Value);
			}
			else
			{
				((HttpHeaders)val.Headers).Add(header.Key, header.Value);
			}
		}
		action("==Request Headers==", LogLevel.L1_Debug);
		foreach (KeyValuePair<string, IEnumerable<string>> item in (HttpHeaders)val.Headers)
		{
			foreach (string item2 in item.Value)
			{
				action(string.Format(Defaults.Culture, "{0} : {1}", item.Key, item2), (item.Key == "Authorization") ? LogLevel.L1_Debug : LogLevel.L0_Info);
			}
		}
		if (val.Content != null && val.Content.Headers != null)
		{
			foreach (KeyValuePair<string, IEnumerable<string>> item3 in (HttpHeaders)val.Content.Headers)
			{
				foreach (string item4 in item3.Value)
				{
					action(string.Format(Defaults.Culture, "{0} : {1}", item3.Key, item4), (item3.Key == "Authorization") ? LogLevel.L1_Debug : LogLevel.L0_Info);
				}
			}
		}
		if (flag)
		{
			action("==Request Body==", LogLevel.L1_Debug);
			action(Encoding.UTF8.GetString(pdxRequest.Body), LogLevel.L1_Debug);
		}
		return val;
	}

	public override async Task<Response> REST(string url, RESTMethod method, Request request = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		FlowData flowData = request?.FlowData;
		Action<string, LogLevel> LogI = delegate(string logMsg, LogLevel level)
		{
			Log(logMsg, level, flowData);
		};
		HttpMethod restVerb = HttpMethod.Get;
		switch (method)
		{
		case RESTMethod.GET:
			restVerb = HttpMethod.Get;
			break;
		case RESTMethod.PUT:
			restVerb = HttpMethod.Put;
			break;
		case RESTMethod.POST:
			restVerb = HttpMethod.Post;
			break;
		case RESTMethod.DELETE:
			restVerb = HttpMethod.Delete;
			break;
		case RESTMethod.PATCH:
			restVerb = new HttpMethod("PATCH");
			break;
		}
		HttpResponseMessage msg;
		try
		{
			LogI("Building request", LogLevel.L1_Debug);
			HttpRequestMessage val = PdxToNetRequest(url, request, restVerb);
			LogI("Sending request", LogLevel.L1_Debug);
			LogI("Awaiting response", LogLevel.L1_Debug);
			TimeSpan delay = timeoutREST;
			if (request != null && request.Timeout > 0)
			{
				delay = new TimeSpan(0, 0, request.Timeout);
			}
			CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(delay);
			CancellationToken token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cancellationTokenSource.Token).Token;
			msg = await ((HttpMessageInvoker)client).SendAsync(val, token).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			LogI($"Exception {ex}", LogLevel.L1_Debug);
			return HTTP.MatchErrorToResponse(isUnityRequestDone: true, isUnityNetworkError: false, isUnityHttpError: false, 0uL, null, ex);
		}
		LogI("Response received, parsing", LogLevel.L1_Debug);
		return await ParseResponse(msg, flowData).ConfigureAwait(continueOnCapturedContext: false);
	}

	public override async Task<Response> GET(string url, Request request = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		return await REST(url, RESTMethod.GET, request).ConfigureAwait(continueOnCapturedContext: false);
	}

	public override async Task<Response> PUT(string url, Request request, CancellationToken cancellationToken = default(CancellationToken))
	{
		return await REST(url, RESTMethod.PUT, request).ConfigureAwait(continueOnCapturedContext: false);
	}

	public override async Task<Response> POST(string url, Request request, CancellationToken cancellationToken = default(CancellationToken))
	{
		return await REST(url, RESTMethod.POST, request).ConfigureAwait(continueOnCapturedContext: false);
	}

	public override async Task<Response> DELETE(string url, Request request = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		return await REST(url, RESTMethod.DELETE, request).ConfigureAwait(continueOnCapturedContext: false);
	}

	public override async Task<Response> PATCH(string url, Request request = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		return await REST(url, RESTMethod.PATCH, request).ConfigureAwait(continueOnCapturedContext: false);
	}

	public override async Task<Response> Download(string url, Request request, IChunkHandler downloadChunkHandler = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		FlowData flowData = request?.FlowData;
		Action<string, LogLevel> LogI = delegate(string logMsg, LogLevel level)
		{
			Log(logMsg, level, flowData);
		};
		if (downloadChunkHandler == null || downloadChunkHandler.Stream == null)
		{
			return await GET(url, request).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (downloadChunkHandler.TransferStatus != null)
		{
			AddOngoingTransfer(downloadChunkHandler.TransferStatus, flowData);
		}
		HttpResponseMessage response;
		try
		{
			HttpRequestMessage val = PdxToNetRequest(url, request, HttpMethod.Get);
			TimeSpan delay = timeoutFileDownload;
			if (request != null && request.Timeout > 0)
			{
				delay = new TimeSpan(0, 0, request.Timeout);
			}
			CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(delay);
			CancellationToken token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cancellationTokenSource.Token).Token;
			response = await client.SendAsync(val, (HttpCompletionOption)1, token).ConfigureAwait(continueOnCapturedContext: false);
			if (response.IsSuccessStatusCode)
			{
				downloadChunkHandler.Initialize();
				long? contentLength = response.Content.Headers.ContentLength;
				if (downloadChunkHandler.TransferStatus != null && contentLength.HasValue && contentLength.Value > 0)
				{
					(downloadChunkHandler.TransferStatus as TransferStatus).SetSize((ulong)contentLength.Value);
				}
				using (Stream remoteStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(continueOnCapturedContext: false))
				{
					byte[] buffer = new byte[32768];
					int size;
					while ((size = await remoteStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)) != 0)
					{
						downloadChunkHandler.HandleBytes(buffer, size);
					}
				}
				response.Content.Dispose();
				response.Content = null;
				if (response.StatusCode == HttpStatusCode.OK)
				{
					response.StatusCode = HttpStatusCode.NoContent;
				}
				downloadChunkHandler.Done();
			}
			else if (response.StatusCode == HttpStatusCode.NotModified)
			{
				downloadChunkHandler.Done();
			}
			else if (downloadChunkHandler.TransferStatus != null)
			{
				(downloadChunkHandler.TransferStatus as TransferStatus).State = ETransferState.Failed;
			}
		}
		catch (Exception otherException)
		{
			if (downloadChunkHandler.TransferStatus != null)
			{
				(downloadChunkHandler.TransferStatus as TransferStatus).State = ETransferState.Failed;
			}
			return HTTP.MatchErrorToResponse(isUnityRequestDone: true, isUnityNetworkError: false, isUnityHttpError: false, 0uL, null, otherException);
		}
		LogI("Response received, parsing", LogLevel.L1_Debug);
		return await ParseResponse(response, flowData).ConfigureAwait(continueOnCapturedContext: false);
	}

	public override async Task<Response> Upload(string url, Request request, IChunkHandler uploadChunkHandler = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		FlowData flowData = request?.FlowData;
		Action<string, LogLevel> LogI = delegate(string logMsg, LogLevel level)
		{
			Log(logMsg, level, flowData);
		};
		if (uploadChunkHandler == null || uploadChunkHandler.Stream == null)
		{
			return await PUT(url, request).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (uploadChunkHandler.TransferStatus != null)
		{
			AddOngoingTransfer(uploadChunkHandler.TransferStatus, flowData);
		}
		HttpResponseMessage msg;
		try
		{
			HttpRequestMessage val = PdxToNetRequest(url, request, HttpMethod.Put, uploadChunkHandler);
			TimeSpan delay = timeoutFileUpload;
			if (request != null && request.Timeout > 0)
			{
				delay = new TimeSpan(0, 0, request.Timeout);
			}
			CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(delay);
			CancellationToken token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cancellationTokenSource.Token).Token;
			msg = await ((HttpMessageInvoker)client).SendAsync(val, token).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception otherException)
		{
			if (uploadChunkHandler.TransferStatus != null)
			{
				(uploadChunkHandler.TransferStatus as TransferStatus).State = ETransferState.Failed;
			}
			return HTTP.MatchErrorToResponse(isUnityRequestDone: true, isUnityNetworkError: false, isUnityHttpError: false, 0uL, null, otherException);
		}
		uploadChunkHandler.Done();
		LogI("Response received, parsing", LogLevel.L1_Debug);
		return await ParseResponse(msg, flowData).ConfigureAwait(continueOnCapturedContext: false);
	}

	public override void AddDefaultHeader(string name, string value)
	{
		((HttpHeaders)client.DefaultRequestHeaders).Add(name, value);
	}

	public override void ClearDefaultHeaders()
	{
		((HttpHeaders)client.DefaultRequestHeaders).Clear();
	}

	public override void SetPDXHeaders(string @namespace, string gameVersion, string sdkVersion, string platform)
	{
		((HttpHeaders)client.DefaultRequestHeaders).Add("X-PDX-SDK-Type", "C#");
		((HttpHeaders)client.DefaultRequestHeaders).Add("X-PDX-SDK-Version", sdkVersion);
		((HttpHeaders)client.DefaultRequestHeaders).Add("X-PDX-Game-Name", @namespace);
		if (!string.IsNullOrEmpty(gameVersion))
		{
			((HttpHeaders)client.DefaultRequestHeaders).Add("X-PDX-Game-Version", gameVersion);
		}
		if (!string.IsNullOrEmpty(platform))
		{
			((HttpHeaders)client.DefaultRequestHeaders).Add("X-PDX-Platform", platform);
		}
	}

	public override int GetTimeoutREST()
	{
		return (int)timeoutREST.TotalSeconds;
	}

	public override int GetTimeoutFileUpload()
	{
		return (int)timeoutFileUpload.TotalSeconds;
	}

	public override int GetTimeoutFileDownload()
	{
		return (int)timeoutFileDownload.TotalSeconds;
	}

	public override void SetTimeoutREST(int timeout)
	{
		timeoutREST = new TimeSpan(0, 0, timeout);
	}

	public override void SetTimeoutFileUpload(int timeout)
	{
		timeoutFileUpload = new TimeSpan(0, 0, timeout);
	}

	public override void SetTimeoutFileDownload(int timeout)
	{
		timeoutFileDownload = new TimeSpan(0, 0, timeout);
	}

	public override List<ITransferStatus> GetTransfers()
	{
		lock (ongoingTransfersLock)
		{
			List<ITransferStatus> list = new List<ITransferStatus>();
			foreach (ITransferStatus ongoingTransfer in ongoingTransfers)
			{
				list.Add(ongoingTransfer);
			}
			return list;
		}
	}

	public override void RemoveOngoingTransfer(ETransferType transferType, ETransferServiceType transferServiceType, string transferId)
	{
		lock (ongoingTransfersLock)
		{
			for (int num = ongoingTransfers.Count - 1; num >= 0; num--)
			{
				ITransferStatus transferStatus = ongoingTransfers[num];
				if (transferStatus.ServiceType == transferServiceType && transferStatus.Type == transferType && transferStatus.Id == transferId)
				{
					ongoingTransfers.RemoveAt(num);
				}
			}
		}
	}

	private void AddOngoingTransfer(ITransferStatus transferStatus, FlowData flowData = null)
	{
		lock (ongoingTransfersLock)
		{
			for (int num = ongoingTransfers.Count - 1; num >= 0; num--)
			{
				ITransferStatus transferStatus2 = ongoingTransfers[num];
				if (transferStatus2.ServiceType == transferStatus.ServiceType && transferStatus2.Type == transferStatus.Type && transferStatus2.Id == transferStatus.Id)
				{
					if (transferStatus2.State == ETransferState.Ongoing)
					{
						Log($"New transfer started while a transfer with same ID is still ongoing. The old transfer status will be overwritten. Service type: {transferStatus2.ServiceType.ToString()}, Transfer ID: {transferStatus2.Id}", LogLevel.L2_Warning, flowData);
					}
					ongoingTransfers.RemoveAt(num);
				}
			}
			ongoingTransfers.Add(transferStatus);
		}
	}
}
