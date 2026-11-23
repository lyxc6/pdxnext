using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Contracts.Service.Mods.Enums;
using PDX.SDK.Contracts.Service.Mods.Models;
using PDX.SDK.Contracts.Service.ThirdParty.Models;
using PDX.SDK.Internal.Service.Mods.Helpers;
using PDX.SDK.Internal.Service.Mods.Models;
using PDX.SDK.Internal.Service.Mods.Request;
using PDX.SDK.Internal.Service.Mods.Response;
using PDX.SDK.Internal.Util;
using PDX.SDK.Util;

namespace PDX.SDK.Internal.Mapper;

internal static class ModMapper
{
	internal static List<ExternalLinkRequest> ToExternalLinkRequest(this List<ExternalLink> externalLinks)
	{
		List<ExternalLinkRequest> list = new List<ExternalLinkRequest>();
		if (externalLinks != null)
		{
			foreach (ExternalLink externalLink in externalLinks)
			{
				list.Add(new ExternalLinkRequest
				{
					type = externalLink.Type,
					url = externalLink.URL,
					displayName = externalLink.DisplayName
				});
			}
		}
		return list;
	}

	internal static List<ExternalLink> ToExternalLink(this List<ExternalLinkResponse> externalLinksRequest)
	{
		List<ExternalLink> list = new List<ExternalLink>();
		if (externalLinksRequest != null)
		{
			foreach (ExternalLinkResponse item in externalLinksRequest)
			{
				list.Add(new ExternalLink
				{
					Type = item.type,
					URL = item.url,
					DisplayName = item.displayName
				});
			}
		}
		return list;
	}

	internal static ForumPost ToForumPost(this ForumPostResponse forumPostResponse)
	{
		return new ForumPost
		{
			Message = forumPostResponse.message,
			Username = forumPostResponse.username,
			UserId = forumPostResponse.userId,
			UserTitle = forumPostResponse.userTitle,
			Url = forumPostResponse.url,
			Created = forumPostResponse.created,
			PostId = forumPostResponse.postId,
			Avatar = forumPostResponse.avatar
		};
	}

	internal static List<PublishDependencyRequest> ToRequestDependencies(this List<ModDependency> dependencies)
	{
		List<PublishDependencyRequest> list = new List<PublishDependencyRequest>();
		if (dependencies != null)
		{
			foreach (ModDependency dependency in dependencies)
			{
				list.Add(new PublishDependencyRequest
				{
					type = dependency.Type.ToDependencyTypeString(),
					value = ((dependency.Type == DependencyType.Dlc) ? dependency.DisplayName : dependency.Id.ToString()),
					version = ((dependency.Type == DependencyType.Mod) ? dependency.Version : null)
				});
			}
		}
		return list;
	}

	internal static ModDetails ToModDetails(this SubscribedModResponse subscribedMod, ILogger logger = null, FlowData fd = null)
	{
		return new ModDetails
		{
			Id = subscribedMod.modId.ParseIntOrDefault(),
			Name = subscribedMod.name,
			DisplayName = subscribedMod.displayName,
			ShortDescription = subscribedMod.shortDescription,
			RequiredGameVersion = subscribedMod.requiredVersion,
			LatestVersion = subscribedMod.latestVersion,
			Version = subscribedMod.preferredVersion,
			ThumbnailPath = subscribedMod.displayImagePath,
			RepositoryPath = subscribedMod.repositoryPath,
			Tags = subscribedMod.tags?.Select((string t) => new ModTag
			{
				Id = t,
				DisplayName = t
			}).ToList(),
			Size = subscribedMod.metadata.ParseSizeInMemory(subscribedMod.modId.ParseIntOrDefault(), logger, fd),
			LatestUpdate = subscribedMod.latestUpdate.ParseDateTime("ModMapper.SubscribedModResponse.ToModDetails", "latestUpdate", "ModMapper", logger, fd),
			State = subscribedMod.state.GetModState(),
			Playsets = new List<PlaysetInMod>(),
			ExternalLinks = new List<ExternalLink>(),
			Author = subscribedMod.author,
			HasLiked = subscribedMod.hasLiked,
			CreationDate = subscribedMod.creationDate.ParseDateTime("ModMapper.SubscribedModResponse.ToModDetails", "creationDate", "ModMapper", logger, fd),
			ThirdPartyProfiles = subscribedMod.thirdPartyLinks?.Select((ThirdPartyLinksResponse x) => x.ToThirdPartyProfile()).ToList()
		};
	}

	internal static PlaysetInMod ToPlaysetInMod(this SubscribedModResponse subscribedMod, ILogger logger = null, FlowData fd = null)
	{
		return new PlaysetInMod
		{
			PlaysetId = subscribedMod.activationSetId,
			Name = subscribedMod.activationSetName,
			ModIsEnabled = subscribedMod.enabled,
			LoadOrder = subscribedMod.activationOrder,
			SubscribedDate = subscribedMod.installed.ParseDateTime("GetAllSubscribed", "installed", "ModsBackendApiService", logger, fd),
			Version = subscribedMod.preferredVersion
		};
	}

	internal static ThirdPartyProfile ToThirdPartyProfile(this ThirdPartyLinksResponse thirdPartyLink)
	{
		ThirdPartyProfile result = null;
		if (thirdPartyLink != null)
		{
			result = new ThirdPartyProfile
			{
				Provider = ProviderExtensions.GetProvider(thirdPartyLink.platform),
				Id = thirdPartyLink.thirdPartyId,
				DisplayName = thirdPartyLink.thirdPartyDisplayName
			};
		}
		return result;
	}

	internal static ModDetails ToModDetails(this ModDetailsResponse modDetailsResponse, ILogger logger = null, FlowData fd = null)
	{
		ModDetails modDetails = new ModDetails();
		modDetails.Id = modDetailsResponse.modId.ParseIntOrDefault();
		modDetails.Name = modDetailsResponse.name;
		modDetails.DisplayName = modDetailsResponse.displayName;
		modDetails.ShortDescription = modDetailsResponse.shortDescription;
		modDetails.LongDescription = modDetailsResponse.longDescription;
		modDetails.RepositoryPath = modDetailsResponse.repositoryPath;
		modDetails.Author = modDetailsResponse.author;
		modDetails.RequiredGameVersion = modDetailsResponse.requiredVersion;
		modDetails.LatestVersion = modDetailsResponse.latestVersion.ToString();
		modDetails.Background = modDetailsResponse.background;
		modDetails.Tags = modDetailsResponse.tags?.Select((string t) => new ModTag
		{
			Id = t,
			DisplayName = t
		}).ToList();
		modDetails.Rating = modDetailsResponse.rating;
		modDetails.RatingsTotal = modDetailsResponse.ratingsTotal;
		modDetails.ForumLinks = ((modDetailsResponse.forumLinks != null) ? modDetailsResponse.forumLinks : new string[1] { modDetailsResponse.forumLink });
		modDetails.ExternalLinks = ((modDetailsResponse.externalLinks == null) ? new List<ExternalLink>() : modDetailsResponse.externalLinks.ToExternalLink());
		modDetails.Size = modDetailsResponse.metadata.ParseSizeInMemory(modDetailsResponse.modId.ParseIntOrDefault(), logger, fd);
		modDetails.State = modDetailsResponse.state.GetModState();
		modDetails.SubscriptionsTotal = modDetailsResponse.subscriptions;
		modDetails.Version = modDetailsResponse.modVersion;
		modDetails.UserModVersion = modDetailsResponse.userModVersion;
		modDetails.HasLiked = modDetailsResponse.hasLiked;
		modDetails.CreationDate = modDetailsResponse.creationDate.ParseDateTime("ModMapper.ModDetailsResponse.ToModDetails", "creationDate", "ModMapper", logger, fd);
		modDetails.LatestUpdate = modDetailsResponse.latestUpdate.ParseDateTime("ModMapper.ModDetailsResponse.ToModDetails", "latestUpdate", "ModMapper", logger, fd);
		ModDetails modDetails2 = modDetails;
		modDetails2.Dependencies = new List<ModDependency>();
		foreach (RequirementResponse requirement in modDetailsResponse.requirements)
		{
			modDetails2.Dependencies.Add(new ModDependency
			{
				Id = requirement.id.ParseIntOrDefault(),
				DisplayName = requirement.name,
				Type = requirement.productType.GetDependencyType(),
				Version = requirement.version.ToString()
			});
		}
		modDetails2.Changelog = new List<Change>();
		foreach (ChangeResponse item in modDetailsResponse.changelog)
		{
			modDetails2.Changelog.Add(new Change
			{
				Version = item.modVersion.ToString(),
				ReleasedDate = item.released.ParseDateTime("Mods.Details", "change.released", "ModMapper", logger, fd),
				Details = item.details,
				Size = item.size,
				UserModVersion = item.userModVersion
			});
		}
		modDetails2.Playsets = new List<PlaysetInMod>(modDetailsResponse.installedActivationSets.Count);
		foreach (PlaysetResponse installedActivationSet in modDetailsResponse.installedActivationSets)
		{
			modDetails2.Playsets.Add(new PlaysetInMod
			{
				PlaysetId = installedActivationSet.id,
				SubscribedDate = installedActivationSet.installed.ParseDateTime("Mods.Details", "set.installed", "ModMapper", logger, fd),
				ModIsEnabled = installedActivationSet.enabled,
				Version = installedActivationSet.preferredVersion
			});
		}
		modDetails2.ThumbnailPath = modDetailsResponse.displayImagePath;
		modDetails2.Screenshots = new List<Screenshot>();
		foreach (ScreenshotResponse screenshot in modDetailsResponse.screenshots)
		{
			modDetails2.Screenshots.Add(new Screenshot
			{
				Image = screenshot.image,
				Thumbnail = screenshot.thumbnail
			});
		}
		modDetails2.ExternalLinks = new List<ExternalLink>();
		if (modDetailsResponse.externalLinks != null)
		{
			foreach (ExternalLinkResponse externalLink in modDetailsResponse.externalLinks)
			{
				modDetails2.ExternalLinks.Add(new ExternalLink(externalLink.type, externalLink.url, externalLink.displayName));
			}
		}
		modDetails2.Actions = new ActionsAllowed
		{
			Edit = modDetailsResponse.actions.canEdit,
			Install = modDetailsResponse.actions.canInstall,
			Enable = modDetailsResponse.actions.canEnable,
			Rate = modDetailsResponse.actions.canRate,
			Upload = modDetailsResponse.actions.canUpload,
			Download = modDetailsResponse.actions.canDownload,
			ViewDisabled = modDetailsResponse.actions.canViewDisabled,
			SelectVersion = modDetailsResponse.actions.canSelectVersion,
			Reorder = modDetailsResponse.actions.canReorder,
			Playsets = modDetailsResponse.actions.activationSets
		};
		modDetails2.ThirdPartyProfiles = modDetailsResponse.thirdPartyLinks?.Select((ThirdPartyLinksResponse x) => x.ToThirdPartyProfile()).ToList();
		return modDetails2;
	}

	internal static ModSearch ToModSearch(this SearchModResponse mod, ILogger logger = null, FlowData fd = null)
	{
		ModSearch modSearch = new ModSearch
		{
			Name = mod.name,
			DisplayName = mod.displayName,
			Author = mod.author,
			ShortDescription = mod.shortDescription,
			RequiredGameVersion = mod.requiredVersion,
			LatestVersion = mod.modVersion,
			Version = mod.preferredVersion,
			ThumbnailPath = mod.displayImagePath,
			IsEnabled = mod.enabled,
			Rating = mod.rating,
			RatingsTotal = mod.ratingsTotal,
			Tags = mod.tags?.Select((string t) => new ModTag
			{
				Id = t,
				DisplayName = t
			}).ToList(),
			State = mod.state.GetModState(),
			InstalledCount = mod.installedCount,
			UserModVersion = mod.userModVersion
		};
		modSearch.Id = mod.modId.ParseIntOrDefault();
		modSearch.Size = mod.metadata.ParseSizeInMemory(modSearch.Id, logger, fd);
		modSearch.IsSubscribed = mod.installed != null;
		modSearch.SubscriptionDate = mod.installed.ParseDateTime("ToModSearch", "installed", "ModMapper", logger, fd);
		modSearch.ThirdPartyProfiles = mod.thirdPartyLinks?.Select((ThirdPartyLinksResponse x) => x.ToThirdPartyProfile()).ToList();
		return modSearch;
	}

	internal static SearchRequest ToSearchRequest(this SearchData searchData, string gameNamespace, string modsPlatform)
	{
		SearchRequest searchRequest = new SearchRequest();
		searchRequest.osList = new string[1] { modsPlatform };
		searchRequest.arch = "Any";
		searchRequest.gameNames = new string[1] { gameNamespace };
		searchRequest.searchQuery = searchData.searchQuery;
		searchRequest.author = searchData.author;
		searchRequest.requiredVersion = null;
		searchRequest.tags = searchData.tags?.ToArray();
		searchRequest.page = searchData.page;
		searchRequest.pageSize = searchData.pageSize;
		searchRequest.sortBy = searchData.sortBy.GetSortString();
		searchRequest.orderBy = searchData.orderBy.GetSearchOrderString();
		searchRequest.time = searchData.time?.GetTimeString();
		searchRequest.onlyModsByMe = searchData.onlyModsByMe;
		searchRequest.onlyInstalled = null;
		searchRequest.includeDependants = false;
		searchRequest.states = searchData.states?.Select((ModState s) => s.GetModState()).ToArray();
		return searchRequest;
	}

	internal static PublishV1Request ToPublishV1Request(this PublishWipData inputData, IMod modData, string gameNamespace)
	{
		return new PublishV1Request
		{
			modName = inputData.wipGuid,
			displayName = modData.DisplayName,
			gameName = gameNamespace,
			os = inputData.os.ToModsPlatformString(),
			arch = "Any",
			recommendedGameVersion = inputData.recommendedGameVersion,
			shortDescription = modData.ShortDescription,
			longDescription = modData.LongDescription,
			tags = (inputData.tags?.ToArray() ?? (from t in modData.Tags?.Where((ModTag t) => !string.IsNullOrEmpty(t.Id))
				select t.Id).ToArray()),
			contentFileName = modData.LocalData.ContentFileOrFolder,
			thumbnail = ((!string.IsNullOrEmpty(modData.LocalData.ThumbnailFilename)) ? Path.GetFileName(modData.LocalData.ThumbnailFilename) : null),
			screenshotNames = modData.LocalData.ScreenshotsFilenames?.Select((string s) => Path.GetFileName(s)).ToArray(),
			backgroundColor = (inputData.backgroundColor ?? "#000000"),
			dependencies = inputData.dependencies.ToRequestDependencies().ToArray(),
			forumLink = inputData.forumLink,
			forumLinks = inputData.forumLinks?.ToArray(),
			externalLinks = inputData.externalLinks.ToExternalLinkRequest().ToArray(),
			userModVersion = inputData.userModVersion
		};
	}

	internal static PublishRequest ToPublishRequest(this PublishWipData inputData, IMod modData, string gameNamespace, string zipContentFileName)
	{
		return new PublishRequest
		{
			modName = inputData.wipGuid,
			displayName = modData.DisplayName,
			gameName = gameNamespace,
			os = inputData.os.ToModsPlatformString(),
			arch = "Any",
			recommendedGameVersion = inputData.recommendedGameVersion,
			shortDescription = modData.ShortDescription,
			longDescription = modData.LongDescription,
			tags = (inputData.tags?.ToArray() ?? (from t in modData.Tags?.Where((ModTag t) => !string.IsNullOrEmpty(t.Id))
				select t.Id).ToArray()),
			contentFileName = zipContentFileName,
			thumbnail = ((!string.IsNullOrEmpty(modData.LocalData.ThumbnailFilename)) ? Path.GetFileName(modData.LocalData.ThumbnailFilename) : null),
			screenshotNames = modData.LocalData.ScreenshotsFilenames?.Select((string s) => Path.GetFileName(s)).ToArray(),
			dependencies = inputData.dependencies.ToRequestDependencies().ToArray(),
			forumLinks = inputData.forumLinks?.ToArray(),
			externalLinks = inputData.externalLinks.ToExternalLinkRequest().ToArray(),
			userModVersion = inputData.userModVersion
		};
	}

	internal static PublishUpdateRequest ToPublishUpdateRequest(this PublishUpdateData inputData, IMod modData, ModDetails modDetails, bool updateContent)
	{
		IEnumerable<string> screenshotNames = inputData.ScreenshotNames;
		IEnumerable<string> localScreenshotNames = screenshotNames ?? modData.LocalData.ScreenshotsFilenames?.Select((string s) => Path.GetFileName(s));
		ExternalLinkRequest[] externalLinks = ((inputData.ExternalLinks == null) ? modDetails.ExternalLinks.ToExternalLinkRequest().ToArray() : inputData.ExternalLinks.ToExternalLinkRequest().ToArray());
		PublishUpdateRequest obj = new PublishUpdateRequest
		{
			displayName = inputData.DisplayName,
			modId = modData.Id.ToString(),
			os = inputData.Os.ToModsPlatformString(),
			arch = "Any",
			recommendedGameVersion = inputData.RecommendedGameVersion,
			shortDescription = modData.ShortDescription,
			longDescription = modData.LongDescription,
			contentFileName = (updateContent ? modData.LocalData.ContentFileOrFolder : null),
			thumbnail = ((!string.IsNullOrEmpty(modData.LocalData.ThumbnailFilename)) ? Path.GetFileName(modData.LocalData.ThumbnailFilename) : null),
			screenshotsToAdd = ((modDetails.Screenshots.Count != 0) ? localScreenshotNames?.Where((string screenshotName) => !modDetails.Screenshots.Any((Screenshot rm) => Path.GetFileName(rm.Image) == screenshotName)).ToArray() : localScreenshotNames?.ToArray())
		};
		List<string> screenshotsFilenames = modData.LocalData.ScreenshotsFilenames;
		obj.screenshotsToRemove = ((screenshotsFilenames != null && screenshotsFilenames.Count == 0) ? modDetails.Screenshots.Select((Screenshot s) => Path.GetFileName(s.Image)).ToArray() : (from s in modDetails.Screenshots?.Where((Screenshot s) => !localScreenshotNames.Contains(Path.GetFileName(s.Image)))
			select Path.GetFileName(s.Image)).ToArray());
		obj.backgroundColor = ((modDetails.Background == "transparent") ? "#000000" : modDetails.Background);
		obj.dependencies = inputData.Dependencies.ToRequestDependencies().ToArray();
		obj.changelogEntry = inputData.Changelog;
		List<string> forumLinks = inputData.ForumLinks;
		obj.forumLink = ((forumLinks != null && forumLinks.Count > 0) ? inputData.ForumLinks[0] : null);
		obj.forumLinksToAdd = ((inputData.ForumLinks == null) ? null : inputData.ForumLinks?.Where((string link) => !modDetails.ForumLinks.Contains(link)).ToArray());
		obj.forumLinksToRemove = ((inputData.ForumLinks == null) ? null : modDetails.ForumLinks?.Where((string link) => !inputData.ForumLinks.Contains(link)).ToArray());
		obj.externalLinks = externalLinks;
		obj.userModVersion = inputData.UserModVersion;
		return obj;
	}

	internal static PublishNewModVersionRequest ToPublishNewModVersionRequest(this PublishUpdateData inputData, IMod modData, string zipContentFileName)
	{
		return new PublishNewModVersionRequest
		{
			displayName = (inputData.DisplayName ?? modData.DisplayName),
			os = inputData.Os.ToModsPlatformString(),
			arch = "Any",
			recommendedGameVersion = (inputData.RecommendedGameVersion ?? modData.RequiredGameVersion),
			shortDescription = modData.ShortDescription,
			longDescription = modData.LongDescription,
			tags = (inputData.Tags?.ToArray() ?? (from t in modData.Tags?.Where((ModTag t) => !string.IsNullOrEmpty(t.Id))
				select t.Id).ToArray()),
			contentFileName = zipContentFileName,
			thumbnail = ((!string.IsNullOrEmpty(modData.LocalData.ThumbnailFilename)) ? Path.GetFileName(modData.LocalData.ThumbnailFilename) : null),
			screenshotNames = (inputData.ScreenshotNames?.ToArray() ?? modData.LocalData.ScreenshotsFilenames?.Select((string s) => Path.GetFileName(s)).ToArray()),
			dependencies = inputData.Dependencies.ToRequestDependencies().ToArray(),
			changelogEntry = inputData.Changelog,
			forumLinks = inputData.ForumLinks?.ToArray(),
			externalLinks = inputData.ExternalLinks?.ToExternalLinkRequest().ToArray(),
			userModVersion = inputData.UserModVersion
		};
	}

	internal static PatchModVersionRequest ToPatchModVersionRequest(this PublishUpdateData inputData, IMod modData)
	{
		return new PatchModVersionRequest
		{
			displayName = inputData.DisplayName,
			recommendedGameVersion = inputData.RecommendedGameVersion,
			shortDescription = modData.ShortDescription,
			longDescription = modData.LongDescription,
			tags = (inputData.Tags?.ToArray() ?? (from t in modData.Tags?.Where((ModTag t) => !string.IsNullOrEmpty(t.Id))
				select t.Id).ToArray()),
			thumbnail = ((!string.IsNullOrEmpty(modData.LocalData.ThumbnailFilename)) ? Path.GetFileName(modData.LocalData.ThumbnailFilename) : null),
			screenshotNames = (inputData.ScreenshotNames?.ToArray() ?? modData.LocalData.ScreenshotsFilenames?.Select((string s) => Path.GetFileName(s)).ToArray()),
			dependencies = inputData.Dependencies.ToRequestDependencies().ToArray(),
			changelogEntry = inputData.Changelog,
			forumLinks = inputData.ForumLinks?.ToArray(),
			externalLinks = inputData.ExternalLinks?.ToExternalLinkRequest().ToArray(),
			userModVersion = inputData.UserModVersion
		};
	}

	internal static ModDownloadMetadata ToModDownloadMetadata(this ModDetails modDetails)
	{
		return new ModDownloadMetadata
		{
			Id = modDetails.Id,
			Version = modDetails.Version,
			Name = modDetails.Name,
			RepositoryPath = modDetails.RepositoryPath,
			ThumbnailPath = modDetails.ThumbnailPath,
			Screenshots = modDetails.Screenshots,
			Size = modDetails.Size
		};
	}

	internal static IMod ToModSubscribed(this SubscribeModResponse subscribeBulkModResponse)
	{
		return new Mod
		{
			Id = subscribeBulkModResponse.id,
			Version = subscribeBulkModResponse.version.ToString()
		};
	}

	internal static Mod ToMod(this ModDetails modDetails)
	{
		return new Mod
		{
			Id = modDetails.Id,
			Name = modDetails.Name,
			Version = modDetails.Version,
			DisplayName = modDetails.DisplayName,
			Author = modDetails.Author,
			ShortDescription = modDetails.ShortDescription,
			LongDescription = modDetails.LongDescription,
			RequiredGameVersion = modDetails.RequiredGameVersion,
			LatestVersion = modDetails.LatestVersion.ToString(),
			UserModVersion = modDetails.UserModVersion,
			ThumbnailPath = modDetails.ThumbnailPath,
			Size = modDetails.Size,
			Tags = modDetails.Tags,
			Rating = modDetails.Rating,
			RatingsTotal = modDetails.RatingsTotal,
			State = modDetails.State,
			LatestUpdate = modDetails.LatestUpdate,
			InstalledDate = modDetails.Playsets.Min((PlaysetInMod p) => p.SubscribedDate),
			LocalData = modDetails.LocalData,
			Playsets = modDetails.Playsets
		};
	}

	internal static ModDetails ToModDetails(this Mod mod)
	{
		return new ModDetails
		{
			Id = mod.Id,
			Name = mod.Name,
			Version = mod.Version,
			DisplayName = mod.DisplayName,
			Author = mod.Author,
			ShortDescription = mod.ShortDescription,
			LongDescription = mod.LongDescription,
			RequiredGameVersion = mod.RequiredGameVersion,
			LatestVersion = mod.LatestVersion,
			ThumbnailPath = mod.ThumbnailPath,
			Size = mod.Size,
			Tags = mod.Tags,
			Rating = mod.Rating,
			RatingsTotal = mod.RatingsTotal,
			State = mod.State,
			LatestUpdate = mod.LatestUpdate,
			LocalData = mod.LocalData,
			UserModVersion = mod.UserModVersion,
			Playsets = mod.Playsets
		};
	}

	internal static IMod ToMod(this PlaysetSubscribedModMetaData subscribedModMetaData)
	{
		return new Mod
		{
			Id = subscribedModMetaData.Id,
			Version = subscribedModMetaData.Version,
			LocalData = new LocalData
			{
				LocalType = LocalType.Subscribed
			}
		};
	}

	internal static IMod ToMod(this PlaysetWipModMetaData wipModMetaData)
	{
		return new Mod
		{
			Name = wipModMetaData.Id,
			LocalData = new LocalData
			{
				LocalType = LocalType.WorkInProgress
			}
		};
	}

	internal static IMod ToMod(this PlaysetUnmanagedModMetaData unmanagedModMetaData)
	{
		return new Mod
		{
			Name = unmanagedModMetaData.Path,
			LocalData = new LocalData
			{
				LocalType = LocalType.Unmanaged
			}
		};
	}

	internal static async Task<ChangeTrackedConcurrentDictionary<TValue>> ToChangeTrackedConcurrentDictionary<TValue>(this Dictionary<string, TValue> dictionary, JournalManager journalManager) where TValue : class
	{
		ChangeTrackedConcurrentDictionary<TValue> data = new ChangeTrackedConcurrentDictionary<TValue>(dictionary, journalManager);
		await data.Initialize().ConfigureAwait(continueOnCapturedContext: false);
		return data;
	}
}
