namespace PDX.SDK.Network.Models;

public class HTTPBodyBaseError
{
	public string category { get; set; }

	public string subCategory { get; set; }

	public string detail { get; set; }

	public object data { get; set; }
}
