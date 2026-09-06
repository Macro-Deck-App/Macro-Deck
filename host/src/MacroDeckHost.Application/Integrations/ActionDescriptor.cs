using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Identity;

namespace MacroDeckHost.Application.Integrations;

public sealed record ActionDescriptor(QualifiedId Id, IIntegration Owner, IActionDefinition Definition)
{
	public string OwnerId => Id.OwnerId;

	public string LocalId => Id.LocalId;
}
