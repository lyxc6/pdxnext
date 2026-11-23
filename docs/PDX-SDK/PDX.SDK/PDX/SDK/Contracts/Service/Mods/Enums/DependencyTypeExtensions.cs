namespace PDX.SDK.Contracts.Service.Mods.Enums;

internal static class DependencyTypeExtensions
{
	internal static string ToDependencyTypeString(this DependencyType type)
	{
		return type switch
		{
			DependencyType.Dlc => "DLC", 
			DependencyType.Mod => "MOD", 
			_ => "", 
		};
	}
}
