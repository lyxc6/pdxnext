using System;
using External.TinyIoc;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Contracts.Service.Mods;
using PDX.SDK.Events;
using PDX.SDK.Internal.Platypatch;
using PDX.SDK.Internal.Platypatch.Pgp;
using PDX.SDK.Internal.Service.Mods;
using PDX.SDK.Internal.Util;

namespace PDX.SDK;

internal static class SdkIoc
{
	internal static void RegisterSdkDependencies(this TinyIoCContainer container, Context sdk)
	{
		container.Register((IFileIO)sdk.FileIO);
		container.Register(sdk.PathFactory);
		container.Register(sdk.Logger);
		container.Register(sdk.Events as IInternalEventHub);
		container.Register(sdk.CancellationTokenRegister);
		container.Register((Func<TinyIoCContainer, NamedParameterOverloads, IRequestBuilderAndResponseParserFactory>)((TinyIoCContainer buildContainer, NamedParameterOverloads overloads) => new RequestBuilderAndResponseParserFactory(sdk)));
		container.Register((IContext)sdk);
		container.Register((Func<TinyIoCContainer, NamedParameterOverloads, IFileDownloader>)((TinyIoCContainer buildContainer, NamedParameterOverloads overloads) => new FileDownloader(buildContainer.Resolve<IRequestBuilderAndResponseParserFactory>(), buildContainer.Resolve<IFileIO>(), Context.DiskFileLocks, buildContainer.Resolve<ICancellationTokenRegister>(), buildContainer.Resolve<IInternalEventHub>(), buildContainer.Resolve<ILogger>(), sdk.Config.ChunkSize.Value)));
		container.Register((Func<TinyIoCContainer, NamedParameterOverloads, IRepoPgpContext>)((TinyIoCContainer buildContainer, NamedParameterOverloads overloads) => new RepoPgpContext(sdk.Config.Environment.Value, buildContainer.Resolve<IMessageValidator>())));
	}

	internal static void RegisterSdkServices(this TinyIoCContainer container, string @namespace)
	{
		container.Register((Func<TinyIoCContainer, NamedParameterOverloads, IModsCache>)((TinyIoCContainer buildContainer, NamedParameterOverloads overloads) => new ModsCache(@namespace, buildContainer.Resolve<IPathFactory>(), buildContainer.Resolve<IFileIO>(), buildContainer.Resolve<ILogger>()))).AsSingleton();
		container.Register<IPlaysetsAndModsOperationCoordinator, PlaysetsAndModsOperationCoordinator>().AsSingleton();
		container.Register<IModsBackendApiService, ModsBackendApiService>().AsSingleton();
		container.Register<IMessageValidator, MessageValidator>();
		container.Register<IFileIntegrityVerifier, FileIntegrityVerifier>();
		container.Register<IFileSystemOperation, FileSystemOperation>();
		container.Register<IRemoteRepositoryFactory, RemoteRepositoryFactory>();
		container.Register<ILocalRepoManagerFactory, LocalRepoManagerFactory>();
		container.Register<IPatchManagerFactory, PatchManagerFactory>();
		container.Register<IExecutor, Executor>();
		container.Register<IModsDownloaderService, ModsDownloaderService>().AsSingleton();
		container.Register<IModsPublishService, ModsPublishService>().AsSingleton();
		container.Register<IModsSubscriptionService, ModsSubscriptionService>().AsSingleton();
		container.Register<IPlaysetAndModsSyncService, PlaysetAndModsSyncService>().AsSingleton();
		container.Register<IModsService, ModsService>().AsSingleton();
	}
}
