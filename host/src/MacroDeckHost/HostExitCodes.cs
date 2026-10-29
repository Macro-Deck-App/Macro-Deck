using MacroDeckHost.Application.Security;
using MacroDeckHost.Infrastructure.Persistence;

namespace MacroDeckHost;

public static class HostExitCodes
{
	// Keep in sync with HOST_RESTART_EXIT_CODE, HOST_CRYPTOGRAPHY_UNAVAILABLE_EXIT_CODE and
	// HOST_DATABASE_FROM_NEWER_VERSION_EXIT_CODE in ui/bootstrapper/src/host.rs.
	public const int RestartRequested = 86;

	public const int CryptographyUnavailable = 87;

	public const int DatabaseFromNewerVersion = 88;

	public const int Failed = 1;

	public static int ForStartupFailure(Exception exception)
	{
		if (exception is DatabaseMigrationException { FromNewerHost: true })
		{
			return DatabaseFromNewerVersion;
		}

		return CryptographyProviderErrors.IsProviderUnreachable(exception) ? CryptographyUnavailable : Failed;
	}
}
