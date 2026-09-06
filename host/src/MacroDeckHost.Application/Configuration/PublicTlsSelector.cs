using System.Globalization;

namespace MacroDeckHost.Application.Configuration;

public enum PublicTlsRejection
{
	None,

	NoCertificate,

	HttpsPortConflictsWithPublicPort,

	HttpsPortConflictsWithLoopbackPort
}

public record PublicTlsSelection(PublicEndpointSet Endpoints, PublicTlsRejection Rejection);

public static class PublicTlsSelector
{
	// HTTPS is on unless the user turned it off. A browser only grants a secure context - and with it the
	// service worker the web client installs from - over HTTPS, so an installation that never opened the
	// network settings would otherwise be permanently uninstallable. A value that does not parse carries
	// no user intent and follows the default; only an explicit "false" keeps HTTPS closed.
	public const bool DefaultTlsEnabled = true;

	public static PublicTlsSelection Resolve(string? enabled,
		string? mode,
		string? httpsPort,
		bool certificateConfigured,
		int publicPort,
		int loopbackPort)
	{
		if (!IsTlsEnabled(enabled))
		{
			return new PublicTlsSelection(PublicEndpointSet.HttpOnly(publicPort), PublicTlsRejection.None);
		}

		if (!certificateConfigured)
		{
			return new PublicTlsSelection(PublicEndpointSet.HttpOnly(publicPort), PublicTlsRejection.NoCertificate);
		}

		if (ParseMode(mode) == PublicTlsMode.Replace)
		{
			return new PublicTlsSelection(PublicEndpointSet.HttpsReplacingHttp(publicPort), PublicTlsRejection.None);
		}

		var port = int.TryParse(httpsPort, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stored) &&
			PublicPortSelector.IsConfigurable(stored)
				? stored
				: BuildConfig.DefaultPublicHttpsPort;

		if (port == publicPort)
		{
			return new PublicTlsSelection(PublicEndpointSet.HttpOnly(publicPort),
				PublicTlsRejection.HttpsPortConflictsWithPublicPort);
		}

		if (port == loopbackPort)
		{
			return new PublicTlsSelection(PublicEndpointSet.HttpOnly(publicPort),
				PublicTlsRejection.HttpsPortConflictsWithLoopbackPort);
		}

		return new PublicTlsSelection(PublicEndpointSet.HttpAndHttps(publicPort, port), PublicTlsRejection.None);
	}

	public static bool IsTlsEnabled(string? enabled)
		=> bool.TryParse(enabled, out var parsed) ? parsed : DefaultTlsEnabled;

	public static PublicTlsMode ParseMode(string? mode)
		=> string.Equals(mode, nameof(PublicTlsMode.Replace), StringComparison.OrdinalIgnoreCase)
			? PublicTlsMode.Replace
			: PublicTlsMode.Additional;
}
