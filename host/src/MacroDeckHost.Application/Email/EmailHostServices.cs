using MacroDeckHost.Application.Integrations;

namespace MacroDeckHost.Application.Email;

public sealed record EmailHostServices(
	IEmailInboxCache Inboxes,
	IEmailInboxKeyStore Keys,
	IIntegrationRegistry Integrations);
