using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Credential;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.Account;
using PDX.SDK.Contracts.Service.Account.Result;
using PDX.SDK.Contracts.Service.Legal.Models;
using PDX.SDK.Contracts.Service.Legal.Result;
using PDX.SDK.Contracts.Service.ThirdParty.Enums;
using PDX.SDK.Contracts.Service.ThirdParty.Models;
using PDX.SDK.Contracts.Service.ThirdParty.Result;
using PDX.SDK.Internal.Cache;
using PDX.SDK.Internal.Mapper;
using PDX.SDK.Internal.Service.Account.Models;
using PDX.SDK.Internal.Service.Account.Request;
using PDX.SDK.Internal.Service.Account.Response;
using PDX.SDK.Internal.Service.Account.Result;
using PDX.SDK.Internal.Service.Legal;
using PDX.SDK.Internal.Service.Telemetry;
using PDX.SDK.Internal.Service.ThirdParty;
using PDX.SDK.Internal.Util;
using PDX.SDK.Network.Enums;
using PDX.SDK.Network.Models;
using PDX.SDK.Util;

namespace PDX.SDK.Internal.Service.Account;

internal class AccountService : InternalService, IAccountService
{
	internal AccountService(Context context)
		: base("Account", context)
	{
	}

	public async Task<CreateResult> Create(EmailAndPasswordCredential emailAndPassword, Language language, Country country, DateTime dateOfBirth)
	{
		return await Create(new CreateAccountInputData
		{
			EmailAndPassword = emailAndPassword,
			Language = language,
			Country = country,
			DateOfBirth = dateOfBirth
		}).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<CreateResult> Create(EmailAndPasswordCredential emailAndPassword, Language language, Country country, DateTime dateOfBirth, string source = null, string referer = null, string sourceService = null, string campaign = null, string medium = null, string channel = null, string firstName = null, string lastName = null, string addressLine1 = null, string addressLine2 = null, string city = null, string state = null, string zipCode = null, string emailTemplate = null, string landingUrl = null, string refererAccount = null, bool marketingPermission = false)
	{
		return await Create(new CreateAccountInputData
		{
			EmailAndPassword = emailAndPassword,
			Language = language,
			Country = country,
			DateOfBirth = dateOfBirth,
			Source = source,
			Referer = referer,
			SourceService = sourceService,
			Campaign = campaign,
			Medium = medium,
			Channel = channel,
			FirstName = firstName,
			LastName = lastName,
			AddressLine1 = addressLine1,
			AddressLine2 = addressLine2,
			City = city,
			State = state,
			ZipCode = zipCode,
			EmailTemplate = emailTemplate,
			LandingUrl = landingUrl,
			RefererAccount = refererAccount,
			MarketingPermission = marketingPermission
		}).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<PDX.SDK.Contracts.Result> UpdateDetails(Language? language = null, Country? country = null, DateTime? dateOfBirth = null, string firstName = null, string lastName = null, string addressLine1 = null, string addressLine2 = null, string city = null, string state = null, string zipCode = null)
	{
		return await UpdateDetails(new UpdateAccountDetailsInputData
		{
			Language = language,
			Country = country,
			DateOfBirth = dateOfBirth,
			FirstName = firstName,
			LastName = lastName,
			AddressLine1 = addressLine1,
			AddressLine2 = addressLine2,
			City = city,
			State = state,
			ZipCode = zipCode
		}).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GetDetailsResult> GetDetails()
	{
		return await GetDetails(null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<ExistsResult> Exists(ICredential credential)
	{
		return await Exists(credential, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<LoginResult> Login(ICredential credential)
	{
		return await Login(credential, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<PDX.SDK.Contracts.Result> Logout()
	{
		return await Logout(null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<PDX.SDK.Contracts.Result> ResetPassword(string email)
	{
		return await ResetPassword(email, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<StartupResult> Startup()
	{
		return await Startup(null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<PDX.SDK.Contracts.Result> ReportPlayer(string reportedAccountId, string category)
	{
		return await ReportPlayer(reportedAccountId, category, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<PDX.SDK.Contracts.Result> ReportPlayer(string reportedAccountId, string category, FlowData flowData = null)
	{
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<GetDetailsResult>("Get account details");
		}
		FlowData fd = new FlowData(flowData);
		Log("Account.ReportPlayer", LogLevel.L1_Debug, fd);
		Dictionary<string, string> body = new Dictionary<string, string>
		{
			{ "reportedAccountId", reportedAccountId },
			{ "category", category }
		};
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath("accounts/report/" + _context.Namespace).SetAuthHeader()
			.SetBody(body)
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
	}

	internal async Task<CreateResult> Create(CreateAccountInputData inputData, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Account.Create(EmailAndPassword)", LogLevel.L1_Debug, fd);
		PDX.SDK.Contracts.Result result = await PerformCreateCall(inputData.ToAccountCreateDescRequest(), fd).ConfigureAwait(continueOnCapturedContext: false);
		if (result.Success)
		{
			LoginResult loginResult = await Login(inputData.EmailAndPassword, fd).ConfigureAwait(continueOnCapturedContext: false);
			if (loginResult != null)
			{
				return new CreateResult(loginResult);
			}
		}
		return result.CastTo<CreateResult>();
	}

	internal async Task<PDX.SDK.Contracts.Result> UpdateDetails(UpdateAccountDetailsInputData inputData, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Account.UpdateDetails", LogLevel.L1_Debug, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn("update account details");
		}
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.PUT).SetPath("accountdetails").SetAuthHeader()
			.SetBody(inputData.ToAccountCreateDetailsRequest())
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
	}

	internal async Task<GetDetailsResult> GetDetails(FlowData flowData = null)
	{
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<GetDetailsResult>("Get account details");
		}
		FlowData fd = new FlowData(flowData);
		Log("Account.GetDetailsResult", LogLevel.L1_Debug, fd);
		PDXResponseParser<GetDetailsResponse> pDXResponseParser = CreateResponseParser<GetDetailsResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetPath("accountdetails").SetAuthHeader()
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		GetDetailsResponse response = pDXResponseParser.GetResponse();
		GetDetailsResult resultObject = pDXResponseParser.GetResultObject<GetDetailsResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		return resultObject.FromGetDetailsResponse(response, _context.Logger, fd);
	}

	internal async Task<LoginResult> Login(ICredential credential, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Account.Login", LogLevel.L1_Debug, fd);
		InternalLoginResult internalLoginResult = await PerformLoginCall(credential, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (internalLoginResult.Success)
		{
			return await LoginSucceeded(internalLoginResult, fd).ConfigureAwait(continueOnCapturedContext: false);
		}
		return internalLoginResult.CastTo<LoginResult>();
	}

	internal async Task<PDX.SDK.Contracts.Result> Logout(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Account.Logout", LogLevel.L1_Debug, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			await _context.ClearLoginSession(hardClear: true).ConfigureAwait(continueOnCapturedContext: false);
			return NotLoggedIn("log out from account");
		}
		PDX.SDK.Network.Models.Response r = await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.DELETE).SetPath("accounts/sessions").AddNamespaceToPath()
			.SetAuthHeader()
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false);
		await _context.ClearLoginSession(hardClear: true).ConfigureAwait(continueOnCapturedContext: false);
		_context.CleanTempFileStorage();
		PDX.SDK.Contracts.Result resultObject = CreateResponseParser(r, fd).GetResultObject();
		if (resultObject.Success)
		{
			return resultObject;
		}
		return new PDX.SDK.Contracts.Result
		{
			Success = false,
			Error = new Error(InternalError.RemoteLogoutFailed, resultObject.Error.Details, resultObject.Error.Raw)
		};
	}

	internal async Task<PDX.SDK.Contracts.Result> ResetPassword(string email, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Account.ResetPassword", LogLevel.L1_Debug, fd);
		string resetPasswordTemplate = GetResetPasswordTemplate();
		PasswordResetRequest body = new PasswordResetRequest
		{
			email = email,
			emailTemplate = resetPasswordTemplate
		};
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath("accounts/password-reset").SetBody(body)
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
	}

	internal async Task<StartupResult> Startup(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Account.Startup", LogLevel.L1_Debug, fd);
		ISessionData stateCache = await _context.StateCache.GetSessionData().ConfigureAwait(continueOnCapturedContext: false);
		IPlayerData playerCache = await _context.StateCache.GetPlayerData().ConfigureAwait(continueOnCapturedContext: false);
		if (!string.IsNullOrEmpty(stateCache.SessionToken) || !string.IsNullOrEmpty(stateCache.RefreshToken))
		{
			LoginResult loginResult = await DoDiskSessionStartup(fd).ConfigureAwait(continueOnCapturedContext: false);
			if (loginResult != null && loginResult.Success)
			{
				return new StartupResult(loginResult, playerCache.ThirdPartyAutoLoginPromptState);
			}
		}
		if (_context.Config.ThirdParty.CheckForThirdPartyAutoLoginPrompt == false || (_context.Config.ThirdParty.AutoLogin == true && playerCache.ThirdPartyAutoLoginPromptState == AutoLoginPromptState.AutoLoginAccepted))
		{
			LoginResult loginResult2 = await DoThirdPartyStartup(fd).ConfigureAwait(continueOnCapturedContext: false);
			if (loginResult2 != null && loginResult2.Success)
			{
				return new StartupResult(loginResult2, playerCache.ThirdPartyAutoLoginPromptState);
			}
		}
		await new AgeCheck(_context).IsUserUnderAgeAsync(flowData).ConfigureAwait(continueOnCapturedContext: false);
		ListUnviewedResult documentsYetToView;
		if (_context.Config.SkipLegalChecksAndEnableTelemetry == true)
		{
			if (_context.Telemetry is TelemetryService)
			{
				await (_context.Telemetry as TelemetryService).SetAllLegalDocumentsViewedDone().ConfigureAwait(continueOnCapturedContext: false);
			}
			documentsYetToView = new ListUnviewedResult
			{
				Success = true,
				Documents = new List<Document>()
			};
		}
		else
		{
			documentsYetToView = await (_context.Legal as LegalService).ListUnviewed(fd).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (!documentsYetToView.Success)
		{
			await _context.ClearLoginSession().ConfigureAwait(continueOnCapturedContext: false);
		}
		else if (documentsYetToView.Documents.Count > 0)
		{
			await _context.StateCache.Player.SetUnviewedDocuments(new List<Document>(documentsYetToView.Documents)).ConfigureAwait(continueOnCapturedContext: false);
		}
		ThirdPartyResult thirdParty = await CheckThirdPartyState().ConfigureAwait(continueOnCapturedContext: false);
		return new StartupResult
		{
			Success = true,
			Error = null,
			LegalDocuments = documentsYetToView.Documents,
			TrustLevel = TrustLevel.Undefined,
			IsLoggedIn = false,
			ThirdParty = thirdParty,
			AutoLoginPromptState = playerCache.ThirdPartyAutoLoginPromptState
		};
	}

	internal async Task<ExistsResult> Exists(ICredential credential, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Account.Exists", LogLevel.L1_Debug, fd);
		PDX.SDK.Contracts.Result result = await TryFetchSession(credential, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (result.Success)
		{
			ExistsResult existsResult = result.CastTo<ExistsResult>();
			existsResult.AccountExists = true;
			return existsResult;
		}
		result = await TryCreateSession(credential, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (result.Success)
		{
			ExistsResult existsResult2 = result.CastTo<ExistsResult>();
			existsResult2.AccountExists = true;
			return existsResult2;
		}
		if (result.Error != BaseCategory.NotAuthorized)
		{
			ExistsResult existsResult3 = result.CastTo<ExistsResult>();
			existsResult3.AccountExists = false;
			return existsResult3;
		}
		return new ExistsResult
		{
			Success = true,
			AccountExists = false,
			Error = null
		};
	}

	internal async Task<InternalLoginResult> PerformLoginCall(ICredential credential, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Account.PerformLoginCall", LogLevel.L0_Info, fd);
		PDXResponseParser<AccountLoginResponse> pDXResponseParser = CreateResponseParser<AccountLoginResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.PUT).SetPath("accounts/sessions").AddNamespaceToPath()
			.SetAuthHeader(credential)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		AccountLoginResponse response = pDXResponseParser.GetResponse();
		InternalLoginResult resultObject = pDXResponseParser.GetResultObject<InternalLoginResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		if (response.session != null)
		{
			if (response.session.trustLevel != null && response.session.trustLevel.name != null)
			{
				resultObject.TrustLevel = TrustLevelHelper.GetTrustLevel(response.session.trustLevel.name);
			}
			resultObject.UserId = response.session.userid;
			resultObject.Namespace = response.session.@namespace;
			resultObject.Token = response.session.token;
			resultObject.RefreshToken = response.session.refresh_token;
		}
		return resultObject;
	}

	internal async Task<PDX.SDK.Contracts.Result> PerformCreateCall(AccountCreateDescRequest desc, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Account.PerformCreateCall(EmailAndPassword)", LogLevel.L1_Debug, fd);
		PDX.SDK.Contracts.Result resultObject = CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath("accounts").SetBody(desc)
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
		_ = resultObject.Success;
		return resultObject;
	}

	private async Task<PDX.SDK.Contracts.Result> TryCreateSession(ICredential credential, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Account.TryCreateSession", LogLevel.L1_Debug, fd);
		return CreateResponseParser<AccountLoginResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.PUT).SetPath("accounts/sessions").AddNamespaceToPath()
			.SetAuthHeader(credential)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject<PDX.SDK.Contracts.Result>();
	}

	private async Task<PDX.SDK.Contracts.Result> TryFetchSession(ICredential credential, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Account.TryFetchSession", LogLevel.L1_Debug, fd);
		return CreateResponseParser<AccountLoginResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetPath("accounts/sessions").AddNamespaceToPath()
			.SetAuthHeader(credential)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject<PDX.SDK.Contracts.Result>();
	}

	private async Task<LoginResult> DoDiskSessionStartup(FlowData flowData = null)
	{
		ISessionData sessionData = await _context.StateCache.GetSessionData().ConfigureAwait(continueOnCapturedContext: false);
		if (!string.IsNullOrEmpty(sessionData.SessionToken))
		{
			LoginResult loginResult = await PerformDiskSessionLogin(new SessionToken(sessionData.SessionToken), flowData).ConfigureAwait(continueOnCapturedContext: false);
			if (loginResult != null && loginResult.Success)
			{
				return loginResult;
			}
		}
		if (!string.IsNullOrEmpty(sessionData.RefreshToken))
		{
			LoginResult loginResult2 = await PerformDiskSessionLogin(new RefreshToken(sessionData.RefreshToken), flowData).ConfigureAwait(continueOnCapturedContext: false);
			if (loginResult2 != null && loginResult2.Success)
			{
				return loginResult2;
			}
		}
		return null;
	}

	private async Task<LoginResult> PerformDiskSessionLogin(ICredential credential, FlowData flowData = null)
	{
		InternalLoginResult internalLoginResult = await PerformLoginCall(credential, flowData).ConfigureAwait(continueOnCapturedContext: false);
		if (internalLoginResult.Success)
		{
			return await LoginSucceeded(internalLoginResult, flowData).ConfigureAwait(continueOnCapturedContext: false);
		}
		return null;
	}

	private async Task<LoginResult> DoThirdPartyStartup(FlowData flowData = null)
	{
		ICredential enabledThirdPartyCredential = _context.CredentialFactory.GetEnabledThirdPartyCredential();
		InternalLoginResult internalLoginResult = await PerformLoginCall(enabledThirdPartyCredential, flowData).ConfigureAwait(continueOnCapturedContext: false);
		if (internalLoginResult.Success)
		{
			return await LoginSucceeded(internalLoginResult, flowData).ConfigureAwait(continueOnCapturedContext: false);
		}
		return null;
	}

	private async Task<LoginResult> LoginSucceeded(InternalLoginResult loginResult, FlowData flowData = null)
	{
		_ = (await _context.StateCache.GetPlayerData().ConfigureAwait(continueOnCapturedContext: false)).UserId;
		await _context.SetLoggedIn(loginResult.UserId, loginResult.Token, loginResult.RefreshToken, loginResult.TrustLevel, loginResult.Namespace, flowData).ConfigureAwait(continueOnCapturedContext: false);
		await new AgeCheck(_context).IsUserUnderAgeAsync(flowData).ConfigureAwait(continueOnCapturedContext: false);
		ListUnviewedResult documentsYetToView;
		if (_context.Config.SkipLegalChecksAndEnableTelemetry == true)
		{
			if (_context.Telemetry is TelemetryService)
			{
				await (_context.Telemetry as TelemetryService).SetAllLegalDocumentsViewedDone().ConfigureAwait(continueOnCapturedContext: false);
			}
			documentsYetToView = new ListUnviewedResult
			{
				Success = true,
				Documents = new List<Document>()
			};
		}
		else
		{
			documentsYetToView = await (_context.Legal as LegalService).ListUnviewed(flowData).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (!documentsYetToView.Success)
		{
			await _context.ClearLoginSession().ConfigureAwait(continueOnCapturedContext: false);
			return documentsYetToView.CastTo<LoginResult>();
		}
		if (documentsYetToView.Documents.Count > 0)
		{
			await _context.StateCache.Player.SetUnviewedDocuments(new List<Document>(documentsYetToView.Documents)).ConfigureAwait(continueOnCapturedContext: false);
		}
		ThirdPartyResult thirdParty = await CheckThirdPartyState(flowData).ConfigureAwait(continueOnCapturedContext: false);
		return new LoginResult
		{
			Success = true,
			LegalDocuments = documentsYetToView.Documents,
			UserId = loginResult.UserId,
			SessionToken = loginResult.Token,
			TrustLevel = loginResult.TrustLevel,
			ThirdParty = thirdParty
		};
	}

	private async Task<ThirdPartyResult> CheckThirdPartyState(FlowData flowData = null)
	{
		ThirdPartyResult thirdparty = new ThirdPartyResult();
		if (_context.Config.ThirdParty.Xbox.Enabled == true)
		{
			GetAccountLinksResult getAccountLinksResult = await (_context.ThirdParty as ThirdPartyService).GetAccountLinks(new XboxLiveCredential(), flowData).ConfigureAwait(continueOnCapturedContext: false);
			if (getAccountLinksResult.Success && getAccountLinksResult.AccountLinks.Count > 0)
			{
				AccountLink accountLink = getAccountLinksResult.AccountLinks.FirstOrDefault((AccountLink a) => a.Provider == Provider.XboxLive);
				if (accountLink != null)
				{
					thirdparty.ParadoxAccountLinks.Add(accountLink);
				}
			}
		}
		if (_context.Config.ThirdParty.Psn.Enabled == true)
		{
			GetAccountLinksResult getAccountLinksResult2 = await (_context.ThirdParty as ThirdPartyService).GetAccountLinks(new PsnCredential(), flowData).ConfigureAwait(continueOnCapturedContext: false);
			if (getAccountLinksResult2.Success && getAccountLinksResult2.AccountLinks.Count > 0)
			{
				AccountLink accountLink2 = getAccountLinksResult2.AccountLinks.FirstOrDefault((AccountLink a) => a.Provider == Provider.PSN);
				if (accountLink2 != null)
				{
					thirdparty.ParadoxAccountLinks.Add(accountLink2);
				}
			}
		}
		if (_context.Config.ThirdParty.Nintendo.Enabled == true)
		{
			GetAccountLinksResult getAccountLinksResult3 = await (_context.ThirdParty as ThirdPartyService).GetAccountLinks(new NintendoAccountCredential(), flowData).ConfigureAwait(continueOnCapturedContext: false);
			if (getAccountLinksResult3.Success && getAccountLinksResult3.AccountLinks.Count > 0)
			{
				AccountLink accountLink3 = getAccountLinksResult3.AccountLinks.FirstOrDefault((AccountLink a) => a.Provider == Provider.NintendoAccount);
				if (accountLink3 != null)
				{
					thirdparty.ParadoxAccountLinks.Add(accountLink3);
				}
			}
		}
		if (_context.Config.ThirdParty.Google.Enabled == true)
		{
			GetAccountLinksResult getAccountLinksResult4 = await (_context.ThirdParty as ThirdPartyService).GetAccountLinks(new GoogleCredential(), flowData).ConfigureAwait(continueOnCapturedContext: false);
			if (getAccountLinksResult4.Success && getAccountLinksResult4.AccountLinks.Count > 0)
			{
				AccountLink accountLink4 = getAccountLinksResult4.AccountLinks.FirstOrDefault((AccountLink a) => a.Provider == Provider.Google);
				if (accountLink4 != null)
				{
					thirdparty.ParadoxAccountLinks.Add(accountLink4);
				}
			}
		}
		if (_context.Config.ThirdParty.Stadia.Enabled == true)
		{
			GetAccountLinksResult getAccountLinksResult5 = await (_context.ThirdParty as ThirdPartyService).GetAccountLinks(new StadiaCredential(), flowData).ConfigureAwait(continueOnCapturedContext: false);
			if (getAccountLinksResult5.Success && getAccountLinksResult5.AccountLinks.Count > 0)
			{
				AccountLink accountLink5 = getAccountLinksResult5.AccountLinks.FirstOrDefault((AccountLink a) => a.Provider == Provider.Stadia);
				if (accountLink5 != null)
				{
					thirdparty.ParadoxAccountLinks.Add(accountLink5);
				}
			}
		}
		if (_context.Config.ThirdParty.Steam.Enabled == true)
		{
			if (!string.IsNullOrEmpty(_context.Config.ThirdParty.Steam.Ticket) && _context.Config.ThirdParty.Steam.AppId.HasValue)
			{
				GetAccountLinksResult getAccountLinksResult6 = await (_context.ThirdParty as ThirdPartyService).GetAccountLinks(new SteamCredential(_context.Config.ThirdParty.Steam.Ticket, _context.Config.ThirdParty.Steam.AppId.GetValueOrDefault()), flowData).ConfigureAwait(continueOnCapturedContext: false);
				if (getAccountLinksResult6.Success && getAccountLinksResult6.AccountLinks.Count > 0)
				{
					AccountLink accountLink6 = getAccountLinksResult6.AccountLinks.FirstOrDefault((AccountLink a) => a.Provider == Provider.Steam);
					if (accountLink6 != null)
					{
						thirdparty.ParadoxAccountLinks.Add(accountLink6);
					}
				}
			}
			else
			{
				if (string.IsNullOrEmpty(_context.Config.ThirdParty.Steam.Ticket))
				{
					_context.Config.Logger.Log("Steam Ticket is not set up", LogLevel.L2_Warning);
				}
				if (!_context.Config.ThirdParty.Steam.AppId.HasValue)
				{
					_context.Config.Logger.Log("Steam App ID is not configured", LogLevel.L2_Warning);
				}
			}
		}
		if (_context.Platform != Platform.iPhoneOS_Handheld && _context.Platform != Platform.iPhoneOS_Tablet)
		{
			GetAccountLinksResult getAccountLinksResult7 = await (_context.ThirdParty as ThirdPartyService).GetAccountLinks().ConfigureAwait(continueOnCapturedContext: false);
			if (getAccountLinksResult7.Success && getAccountLinksResult7.AccountLinks.Count > 0)
			{
				thirdparty.ThirdPartyAccountLinks = getAccountLinksResult7.AccountLinks;
			}
		}
		return thirdparty;
	}

	private string GetResetPasswordTemplate()
	{
		return _context.Config.Environment switch
		{
			BackendEnvironment.Test => "customer-portal-test", 
			BackendEnvironment.Staging => "customer-portal-staging", 
			BackendEnvironment.Sandbox => "customer-portal-sandbox", 
			BackendEnvironment.Live => "customer-portal-prod", 
			_ => "customer-portal-sandbox", 
		};
	}
}
