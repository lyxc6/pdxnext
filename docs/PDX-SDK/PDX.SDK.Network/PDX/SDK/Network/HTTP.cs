using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Network;
using PDX.SDK.Contracts.Util;
using PDX.SDK.Network.Enums;
using PDX.SDK.Network.Models;
using PDX.SDK.Network.Util;

namespace PDX.SDK.Network;

public abstract class HTTP
{
	public const int DefaultErrorCode = 418;

	public abstract Task<Response> REST(string url, RESTMethod method, Request request = null, CancellationToken cancellationToken = default(CancellationToken));

	public abstract Task<Response> GET(string url, Request request = null, CancellationToken cancellationToken = default(CancellationToken));

	public abstract Task<Response> PUT(string url, Request request, CancellationToken cancellationToken = default(CancellationToken));

	public abstract Task<Response> POST(string url, Request request, CancellationToken cancellationToken = default(CancellationToken));

	public abstract Task<Response> DELETE(string url, Request request = null, CancellationToken cancellationToken = default(CancellationToken));

	public abstract Task<Response> PATCH(string url, Request request = null, CancellationToken cancellationToken = default(CancellationToken));

	public abstract Task<Response> Download(string url, Request request, IChunkHandler downloadChunkHandler = null, CancellationToken cancellationToken = default(CancellationToken));

	public abstract Task<Response> Upload(string url, Request request, IChunkHandler uploadChunkHandler = null, CancellationToken cancellationToken = default(CancellationToken));

	public abstract void AddDefaultHeader(string name, string value);

	public abstract void ClearDefaultHeaders();

	public abstract void SetPDXHeaders(string @namespace, string gameVersion, string sdkVersion, string platform);

	public abstract int GetTimeoutREST();

	public abstract int GetTimeoutFileUpload();

	public abstract int GetTimeoutFileDownload();

	public abstract void SetTimeoutREST(int timeout);

	public abstract void SetTimeoutFileUpload(int timeout);

	public abstract void SetTimeoutFileDownload(int timeout);

	public abstract List<ITransferStatus> GetTransfers();

	public abstract void RemoveOngoingTransfer(ETransferType transferType, ETransferServiceType transferServiceType, string transferId);

	protected static Response MatchErrorToResponse(bool isUnityRequestDone = true, bool isUnityNetworkError = false, bool isUnityHttpError = false, ulong downloadedBytes = 0uL, string unityErrorMessage = null, Exception otherException = null, int? statusCode = null)
	{
		int? statusCode2 = ((statusCode.HasValue && statusCode >= 400) ? statusCode : new int?(418));
		bool flag = unityErrorMessage?.ToLower().Contains("abort") ?? false;
		bool flag2 = unityErrorMessage?.ToLower().Contains("unable to write data") ?? false;
		if (isUnityNetworkError)
		{
			if (flag2)
			{
				if (downloadedBytes != 0)
				{
					return Response.CreateErrorResponse(InternalError.DiskFullError.GenerateErrorBody(unityErrorMessage), statusCode2);
				}
				return Response.CreateErrorResponse(InternalError.FileIOError.GenerateErrorBody(unityErrorMessage), statusCode2);
			}
			return Response.CreateErrorResponse(InvalidNetworkResponse.NetworkError.GenerateErrorBody(unityErrorMessage), statusCode2);
		}
		if (otherException != null)
		{
			if (!(otherException is TaskCanceledException ex))
			{
				if (!(otherException is WebException ex2))
				{
					HttpRequestException ex3 = (HttpRequestException)(object)((otherException is HttpRequestException) ? otherException : null);
					if (ex3 == null)
					{
						if (!(otherException is IOException ex4))
						{
							if (otherException is NotSupportedException ex5)
							{
								return Response.CreateErrorResponse(InternalError.DiskPermissionError.GenerateErrorBody(ex5.ToString()), statusCode2);
							}
							return Response.CreateErrorResponse(InvalidNetworkResponse.NetworkError.GenerateErrorBody(otherException.ToString()), statusCode2);
						}
						if (ex4.HResult == -2147024857 || ex4.HResult == -2147024784)
						{
							return Response.CreateErrorResponse(InternalError.DiskFullError.GenerateErrorBody(ex4.ToString()), statusCode2);
						}
						return Response.CreateErrorResponse(InternalError.FileIOError.GenerateErrorBody(ex4.ToString()), statusCode2);
					}
					return Response.CreateErrorResponse(InvalidNetworkResponse.HttpRequestError.GenerateErrorBody(((object)ex3).ToString()), statusCode2);
				}
				return Response.CreateErrorResponse(InvalidNetworkResponse.HttpRequestError.GenerateErrorBody(ex2.ToString()), statusCode2);
			}
			return Response.CreateErrorResponse(InvalidNetworkResponse.TaskTimeoutError.GenerateErrorBody(ex.ToString()), statusCode2);
		}
		if (isUnityHttpError && (downloadedBytes == 0 || flag))
		{
			return Response.CreateErrorResponse(InvalidNetworkResponse.HttpRequestError.GenerateErrorBody(unityErrorMessage), statusCode2);
		}
		if (!isUnityRequestDone)
		{
			return Response.CreateErrorResponse(InvalidNetworkResponse.TaskTimeoutError.GenerateErrorBody("UnityWebRequest.SendWebRequest timed out: " + unityErrorMessage), statusCode2);
		}
		return null;
	}
}
