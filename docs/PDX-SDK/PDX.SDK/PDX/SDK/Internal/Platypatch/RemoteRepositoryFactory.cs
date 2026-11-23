using System.Threading.Tasks;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Internal.Platypatch.Pgp;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Platypatch;

[Preserve]
internal class RemoteRepositoryFactory : IRemoteRepositoryFactory
{
	private readonly IRequestBuilderAndResponseParserFactory _requestBuilderAndResponseParserFactory;

	private readonly IRepoPgpContext _repoPgpContext;

	private readonly IFileDownloader _fileDownloader;

	private readonly IPathFactory _pathFactory;

	private readonly IFileIntegrityVerifier _fileIntegrityVerifier;

	private readonly IMessageValidator _messageValidator;

	[Preserve]
	public RemoteRepositoryFactory(IRequestBuilderAndResponseParserFactory requestBuilderAndResponseParserFactory, IRepoPgpContext repoPgpContext, IFileDownloader fileDownloader, IPathFactory pathFactory, IFileIntegrityVerifier fileIntegrityVerifier, IMessageValidator messageValidator)
	{
		_requestBuilderAndResponseParserFactory = requestBuilderAndResponseParserFactory;
		_repoPgpContext = repoPgpContext;
		_fileDownloader = fileDownloader;
		_pathFactory = pathFactory;
		_fileIntegrityVerifier = fileIntegrityVerifier;
		_messageValidator = messageValidator;
	}

	public async Task<IRemoteRepository> Create(string repositoryName)
	{
		RemoteRepository remoteRepo = new RemoteRepository(repositoryName, _requestBuilderAndResponseParserFactory, _repoPgpContext, _fileDownloader, _pathFactory, _fileIntegrityVerifier, _messageValidator);
		await remoteRepo.Initialize().ConfigureAwait(continueOnCapturedContext: false);
		return remoteRepo;
	}
}
