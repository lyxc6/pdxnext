namespace PDX.SDK.Internal.Platypatch.Models;

internal class ManifestFileEntry
{
	public string RelativePath { get; set; }

	public long Size { get; set; }

	public byte[] Hash { get; set; }

	public ManifestFileEntryFlags Flags { get; set; }

	public long CompressedSize { get; set; }

	public string CompressionType { get; set; }
}
