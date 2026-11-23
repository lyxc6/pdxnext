namespace PDX.SDK.Contracts.Enums.Errors;

public enum InvalidNetworkResponse
{
	MalformedJSON,
	HttpRequestError,
	NetworkError,
	TaskTimeoutError,
	UnexpectedResponseError,
	NoResponseBody,
	ModDownloadFailedError
}
