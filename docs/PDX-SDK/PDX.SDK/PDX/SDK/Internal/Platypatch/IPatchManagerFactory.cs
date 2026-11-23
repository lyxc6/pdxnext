using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Platypatch;

[Preserve]
internal interface IPatchManagerFactory
{
	IPatchManager Create(string patchZipPath);
}
