using MacroDeckHost.Integrations.Http.Client;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Http.Actions;

internal static class HttpActions
{
	public static IReadOnlyList<IActionDefinition> Create(IHttpRequestClient client, HttpVariableAccessor variables) =>
	[
		new SendRequestActionDefinition(client, variables),
		new GetJsonValueActionDefinition(client, variables)
	];
}
