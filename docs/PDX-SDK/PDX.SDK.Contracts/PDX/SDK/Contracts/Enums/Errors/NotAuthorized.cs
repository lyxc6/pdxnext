namespace PDX.SDK.Contracts.Enums.Errors;

public enum NotAuthorized
{
	ExpiredRefreshToken,
	ExpiredSessionToken,
	SessionTooLowTrustLevel,
	InvalidAuthentication,
	RejectedByThirdParty,
	ForbiddenAccess,
	AutomaticLoginFailure
}
