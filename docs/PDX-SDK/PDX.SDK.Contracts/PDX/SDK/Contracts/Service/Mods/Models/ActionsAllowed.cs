namespace PDX.SDK.Contracts.Service.Mods.Models;

public class ActionsAllowed
{
	public bool Edit { get; set; }

	public bool Install { get; set; }

	public bool Enable { get; set; }

	public bool Rate { get; set; }

	public bool Upload { get; set; }

	public bool Download { get; set; }

	public bool ViewDisabled { get; set; }

	public bool SelectVersion { get; set; }

	public bool Reorder { get; set; }

	public bool Playsets { get; set; }
}
