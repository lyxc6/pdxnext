namespace PDX.SDK.Internal.Platypatch.Models;

internal enum PatchOperationType
{
	Unknown,
	Delete,
	New,
	Patch,
	CreateDirectory,
	Symlink,
	UpdateFlags
}
