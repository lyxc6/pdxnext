using System;

namespace PDX.SDK.Internal.Platypatch.Models;

[Flags]
internal enum ManifestFileEntryFlags
{
	None = 0,
	Directory = 1,
	Readable = 2,
	Writable = 4,
	Executable = 8,
	Compressed = 0x10,
	Symlink = 0x20
}
