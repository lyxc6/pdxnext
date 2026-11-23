using System.Collections.Generic;

namespace PDX.SDK.Internal.Service.Mods.Models;

public class PlaysetMetaData
{
	public int ContractFormatVersion { get; private set; }

	public PlaysetGeneralMetaData GeneralData { get; set; }

	public Dictionary<string, PlaysetSubscribedModMetaData> SubscribedMods { get; set; }

	public Dictionary<string, PlaysetWipModMetaData> WipMods { get; set; }

	public Dictionary<string, PlaysetUnmanagedModMetaData> UnmanagedMods { get; set; }
}
