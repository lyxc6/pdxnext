using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Internal.Platypatch.Models;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Platypatch;

[Preserve]
internal class FileIntegrityVerifier : IFileIntegrityVerifier
{
	private readonly IFileIO _fileIo;

	[Preserve]
	public FileIntegrityVerifier(IFileIO fileIo)
	{
		_fileIo = fileIo;
	}

	public bool CheckIntegrity(FolderManifest manifest, ManifestFileEntry fileEntry, string absolutePath, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		if (!_fileIo.PathExists(absolutePath))
		{
			return false;
		}
		HashAlgorithm hashAlgorithm = HashAlgorithm.Create(manifest.HashAlgorithm);
		if (hashAlgorithm == null)
		{
			throw new Exception("Unsupported hash algoritm " + manifest.HashAlgorithm);
		}
		using Stream inputStream = (_fileIo as FileIO).CreateReadStream(absolutePath, flowData2);
		byte[] second = hashAlgorithm.ComputeHash(inputStream);
		return fileEntry.Hash.SequenceEqual(second);
	}
}
