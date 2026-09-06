namespace MacroDeckHost.Domain.Enums;

public enum AuthError
{
	ValidationError,
	InvalidCredentials,
	SetupAlreadyComplete,
	InvalidRefreshToken,
	RefreshTokenReused,
	Throttled
}
