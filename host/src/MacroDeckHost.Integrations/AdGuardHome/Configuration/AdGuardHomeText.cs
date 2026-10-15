using MacroDeck.Localization;
using MacroDeckHost.Integrations.AdGuardHome.State;
using Errors = MacroDeckHost.Localization.AppStrings.Integrations.AdGuardHome.Errors;

namespace MacroDeckHost.Integrations.AdGuardHome.Configuration;

internal static class AdGuardHomeText
{
	public static LocalizedText ConnectionError(AdGuardHomeConnection failure)
	{
		return failure switch
		{
			AdGuardHomeConnection.Unauthorized => Errors.Unauthorized(),
			AdGuardHomeConnection.Timeout => Errors.Timeout(),
			AdGuardHomeConnection.Incompatible => Errors.Incompatible(),
			AdGuardHomeConnection.Redirected => Errors.Redirected(),
			_ => Errors.Unreachable()
		};
	}
}
