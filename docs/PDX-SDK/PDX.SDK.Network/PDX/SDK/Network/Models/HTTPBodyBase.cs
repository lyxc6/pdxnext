namespace PDX.SDK.Network.Models;

public class HTTPBodyBase
{
	public string requestId { get; set; }

	public string result { get; set; }

	public string errorCode { get; set; }

	public string errorMessage { get; set; }

	public string details { get; set; }

	public string message { get; set; }

	public HTTPBodyBaseError error { get; set; }
}
