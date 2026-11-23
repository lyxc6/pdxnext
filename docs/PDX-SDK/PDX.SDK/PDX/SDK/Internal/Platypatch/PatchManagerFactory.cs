using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Platypatch;

[Preserve]
internal class PatchManagerFactory : IPatchManagerFactory
{
	private readonly IFileIO _fileIo;

	private readonly IPathFactory _pathFactory;

	private readonly IFileSystemOperation _fileSystemOperation;

	private readonly ILogger _logger;

	[Preserve]
	public PatchManagerFactory(IFileIO fileIO, IPathFactory pathFactory, IFileSystemOperation fileSystemOperation, ILogger logger = null)
	{
		_fileIo = fileIO;
		_pathFactory = pathFactory;
		_fileSystemOperation = fileSystemOperation;
		_logger = logger;
	}

	public IPatchManager Create(string patchZipPath)
	{
		return new PatchManager(patchZipPath, _fileIo, _pathFactory, _fileSystemOperation, _logger);
	}
}
