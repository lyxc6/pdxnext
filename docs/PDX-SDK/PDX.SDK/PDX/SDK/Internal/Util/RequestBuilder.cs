using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Credential;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Network.Enums;
using PDX.SDK.Network.Models;
using PDX.SDK.Network.Util;
using TinyJson;

namespace PDX.SDK.Internal.Util;

internal class RequestBuilder
{
	internal Context _context;

	private static byte[] _defaultJsonBody = Encoding.UTF8.GetBytes("{}");

	private RESTMethod _method;

	private ContentType _contentType;

	private FlowData _flowData;

	private Request _request = new Request();

	private HostTypes.HostType _hostType;

	private string _path;

	private readonly Dictionary<string, string> _pathParameters = new Dictionary<string, string>();

	private string _error;

	private ChunkHandler _chunkHandler;

	private List<Func<RESTMethod, Task>> _sendCallbackList = new List<Func<RESTMethod, Task>>();

	private CancellationToken _cancellationToken;

	internal RequestBuilder(Context context)
	{
		_context = context;
	}

	public RequestBuilder(Context context, FlowData fd)
		: this(context)
	{
		_flowData = new FlowData(fd);
	}

	public RequestBuilder SetRestMethod(RESTMethod restMethod)
	{
		_method = restMethod;
		return this;
	}

	public RequestBuilder SetPath(string path)
	{
		_path = path;
		return this;
	}

	public RequestBuilder AddToPath(string pathPart)
	{
		if (_path == null)
		{
			_path = pathPart;
		}
		else
		{
			_path = _path.TrimEnd('/') + "/" + pathPart;
		}
		return this;
	}

	public RequestBuilder AddNamespaceToPath()
	{
		return AddToPath(_context.Namespace);
	}

	public RequestBuilder AddParameterToPath(string key, string value)
	{
		if (value == null || string.IsNullOrEmpty(key))
		{
			return this;
		}
		_pathParameters.Add(key, value);
		return this;
	}

	public RequestBuilder SetContentType(ContentType type)
	{
		_contentType = type;
		return this;
	}

	public RequestBuilder AddHeader(string key, string value)
	{
		if (string.IsNullOrEmpty(key))
		{
			return this;
		}
		_request.Headers.Add(new Header
		{
			Key = key,
			Value = value
		});
		return this;
	}

	public RequestBuilder SetAuthHeader(string auth)
	{
		if (auth != null)
		{
			return AddHeader("Authorization", auth);
		}
		return this;
	}

	public RequestBuilder SetAuthHeader(ICredential credential)
	{
		return _context.RequestBuilderCredentialBridge.SetAuthHeader(this, credential);
	}

	public RequestBuilder SetAuthHeader()
	{
		return SetAuthHeader(new SessionToken());
	}

	public RequestBuilder SetBody(string body)
	{
		if (_contentType == ContentType.Undefined)
		{
			_contentType = ContentType.JSON;
		}
		_request.SetBody(body);
		return this;
	}

	public RequestBuilder SetBody(byte[] body)
	{
		_request.SetBody(body);
		return this;
	}

	public RequestBuilder SetBody(object body)
	{
		if (_contentType == ContentType.Undefined)
		{
			_contentType = ContentType.JSON;
		}
		_request.SetBody(body.ToJson());
		return this;
	}

	public RequestBuilder SetBody(ICredential credential)
	{
		return _context.RequestBuilderCredentialBridge.SetBody(this, credential);
	}

	public RequestBuilder SetHostType(HostTypes.HostType hostType)
	{
		_hostType = hostType;
		return this;
	}

	public RequestBuilder SetError(Enum subCategory, string details)
	{
		_error = subCategory.GenerateErrorBody(details);
		return this;
	}

	public RequestBuilder SetError(string error)
	{
		_error = error;
		return this;
	}

	public Request GetRequest()
	{
		return _request;
	}

	public string GetURL()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(HostTypes.GetHostName(_context.Config.Environment, _context.Logger, _hostType).TrimEnd('/'));
		if (string.IsNullOrEmpty(_path))
		{
			return stringBuilder.ToString();
		}
		if (_hostType != HostTypes.HostType.None)
		{
			stringBuilder.Append("/");
		}
		stringBuilder.Append(_path.TrimStart('/'));
		if (_pathParameters.Count <= 0)
		{
			return stringBuilder.ToString();
		}
		stringBuilder.Append("?");
		string value = string.Join("&", _pathParameters.Select((KeyValuePair<string, string> p) => p.Key + "=" + p.Value));
		stringBuilder.Append(value);
		return stringBuilder.ToString();
	}

	public RequestBuilder SetChunkHandler(ChunkHandler chunkHandler)
	{
		_chunkHandler = chunkHandler;
		return this;
	}

	public RequestBuilder SetCancellationToken(CancellationToken token)
	{
		_cancellationToken = token;
		return this;
	}

	public async Task<Response> Send(string url = null)
	{
		foreach (Func<RESTMethod, Task> sendCallback in _sendCallbackList)
		{
			await sendCallback(_method).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (_error != null)
		{
			return Response.CreateErrorResponse(_error);
		}
		if ((_method == RESTMethod.POST || _method == RESTMethod.PUT || _method == RESTMethod.DELETE) && _request.Body == null && (_chunkHandler == null || _chunkHandler.Stream == null))
		{
			_request.Body = _defaultJsonBody;
			_contentType = ContentType.JSON;
		}
		if (_contentType != ContentType.Undefined)
		{
			_request.Headers.Add(new Header
			{
				Key = "Content-Type",
				Value = _contentType.ToContentTypeString()
			});
		}
		_request.FlowData = _flowData;
		if (_chunkHandler != null)
		{
			if (_chunkHandler is DownloadChunkHandler)
			{
				return await _context.HTTP.Download(GetURL(), _request, _chunkHandler, _cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (_chunkHandler is UploadChunkHandler)
			{
				return await _context.HTTP.Upload(GetURL(), _request, _chunkHandler, _cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		return await _context.HTTP.REST(GetURL(), _method, _request, _cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal void RegisterOnSendCallback(Func<RESTMethod, Task> callback)
	{
		_sendCallbackList.Add(callback);
	}
}
