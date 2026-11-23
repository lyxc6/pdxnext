using System.Globalization;

namespace PDX.SDK.Contracts.StandardConstantValues;

public static class Defaults
{
	public static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

	public const int MinRecommendedChunkSize = 32768;
}
