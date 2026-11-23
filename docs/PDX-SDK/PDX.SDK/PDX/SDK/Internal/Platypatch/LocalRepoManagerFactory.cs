using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Platypatch;

[Preserve]
internal class LocalRepoManagerFactory : ILocalRepoManagerFactory
{
	private readonly IFileIO _fileIo;

	private readonly IFileSystemOperation _fileSystemOperation;

	private readonly ILogger _logger;

	[Preserve]
	public LocalRepoManagerFactory(IFileIO fileIo, IFileSystemOperation fileSystemOperation, ILogger logger = null)
	{
		_fileIo = fileIo;
		_fileSystemOperation = fileSystemOperation;
		_logger = logger;
	}

	public ILocalRepoManager Create(string absoluteRepoPath, string appName, string version)
	{
		return new LocalRepoManager(absoluteRepoPath, appName, version, _fileIo, _fileSystemOperation, _logger);
	}
}
