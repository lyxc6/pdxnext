using System;

namespace PDX.SDK.Contracts.Service.DataStorage.CloudSave.Models;

public class Entry
{
	public string Hash { get; set; }

	public string Path { get; set; }

	public bool IsPublic { get; set; }

	public string UserAgent { get; set; }

	public DateTime? Modified { get; set; }

	public string Id { get; set; }

	public bool IsOfficial { get; set; }

	public string Owner { get; set; }

	public int ContentLength { get; set; }

	public string Game { get; set; }

	public DateTime? Created { get; set; }
}
