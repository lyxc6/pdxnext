using System;
using PDX.SDK.Contracts.Service.Generic;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public class PlaysetWipMod : IPlaysetMod
{
	public string Id { get; set; }

	public bool IsEnabled { get; set; }

	public string DisplayName { get; set; }

	public int LoadOrder { get; set; }

	public ulong Size { get; set; }

	public DateTime? Updated { get; set; }

	public ITag[] Tags { get; set; }

	public string DisplayImagePath { get; set; }

	public LocalData LocalData { get; set; }
}
