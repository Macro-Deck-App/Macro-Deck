using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;

public sealed record RemoteActionDescriptor(
	string LocalId,
	LocalizedText Name,
	LocalizedText Description,
	IReadOnlyList<ActionParameter> Parameters,
	string? DescriptiveUiSchema,
	bool SupportsDynamicOptions,
	bool ProvidesState,
	bool ConfiguresWithUiTree = false,
	bool ProvidesIcon = false);
