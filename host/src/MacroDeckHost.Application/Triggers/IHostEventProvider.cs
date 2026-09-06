using MacroDeck.Localization;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Application.Triggers;

public interface IHostEventProvider
{
	string ProviderId { get; }

	LocalizedText ProviderName { get; }

	IReadOnlyList<EventDefinition> EventDefinitions { get; }
}
