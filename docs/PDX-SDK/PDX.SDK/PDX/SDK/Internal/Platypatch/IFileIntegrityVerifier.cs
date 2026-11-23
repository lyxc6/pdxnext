using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Internal.Platypatch.Models;

namespace PDX.SDK.Internal.Platypatch;

[Preserve]
internal interface IFileIntegrityVerifier
{
	bool CheckIntegrity(FolderManifest manifest, ManifestFileEntry fileEntry, string absolutePath, FlowData flowData = null);
}
