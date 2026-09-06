using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Application.Configuration;

public static class NetworkListenerIdentity
{
	public static bool TlsOrCertificateDiffers(NetworkSettings settings)
	{
		if (settings.PublicListenerUnavailable)
		{
			return false;
		}

		var configuredMode = settings.TlsEnabled ? settings.TlsMode : PublicTlsMode.Disabled;

		var configuredHttpsPort = settings.TlsEnabled
			? settings.TlsMode == PublicTlsMode.Replace ? settings.PublicPort : settings.TlsHttpsPort
			: (int?)null;

		if (configuredMode != settings.ActiveTlsMode || configuredHttpsPort != settings.ActiveTlsHttpsPort)
		{
			return true;
		}

		return configuredMode != PublicTlsMode.Disabled &&
			!string.Equals(settings.TlsCertificateFingerprint,
				settings.ActiveTlsCertificateFingerprint,
				StringComparison.Ordinal);
	}
}
