using MacroDeck.Sdk;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.Integrations;

internal static class ProviderDisplayName
{
	public static LocalizedText Resolve(string? providerName, IIntegration integration)
		=> string.IsNullOrEmpty(providerName) ? integration.Name : providerName;
}
