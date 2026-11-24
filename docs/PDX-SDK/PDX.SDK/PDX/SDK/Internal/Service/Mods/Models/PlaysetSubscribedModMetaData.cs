namespace PDX.SDK.Internal.Service.Mods.Models;

public class PlaysetSubscribedModMetaData : IPlaysetModMetaData
{
	public int Id { get; set; }

	public string Version { get; set; }

	public int LoadOrder { get; set; }

	public bool IsEnabled { get; set; }

	public override bool Equals(object obj)
	{
		if (obj == null)
		{
			return false;
		}
		if (!(obj is PlaysetSubscribedModMetaData))
		{
			return false;
		}
		if (Id == ((PlaysetSubscribedModMetaData)obj).Id && Version == ((PlaysetSubscribedModMetaData)obj).Version && LoadOrder == ((PlaysetSubscribedModMetaData)obj).LoadOrder)
		{
			return IsEnabled == ((PlaysetSubscribedModMetaData)obj).IsEnabled;
		}
		return false;
	}

	public override int GetHashCode()
	{
		return Id.GetHashCode() ^ Version.GetHashCode() ^ LoadOrder.GetHashCode() ^ IsEnabled.GetHashCode();
	}
}
