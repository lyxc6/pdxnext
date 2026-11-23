namespace PDX.SDK.Contracts.Service.DataStorage.KeyValue.Models;

public class Entry
{
	public string Key { get; set; }

	public string Value { get; set; }

	public string Timestamp { get; }

	public Entry(string key)
	{
		Key = key;
	}

	public Entry(string key, string value)
	{
		Key = key;
		Value = value;
	}

	public Entry(string key, string value, string timestamp)
	{
		Key = key;
		Value = value;
		Timestamp = timestamp;
	}
}
