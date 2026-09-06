using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Identity;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.Triggers;

public sealed record EventDefinitionDescriptor(
	QualifiedId Id,
	string ProviderId,
	LocalizedText ProviderName,
	bool IsIntegrationProvider,
	EventDefinition Definition);
