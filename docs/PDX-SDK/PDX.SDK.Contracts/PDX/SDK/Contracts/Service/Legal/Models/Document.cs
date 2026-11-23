using PDX.SDK.Contracts.Enums;

namespace PDX.SDK.Contracts.Service.Legal.Models;

public class Document
{
	public string Title { get; set; }

	public string Type { get; set; }

	public string Text { get; set; }

	public Language Language { get; set; }

	public string URL { get; set; }
}
