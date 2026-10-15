using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Http.Protocol;

namespace MacroDeckHost.Integrations.Http.Actions;

internal static class HttpActions
{
	public static IReadOnlyList<IActionDefinition> Create(IHttpRequestClient client, HttpVariableAccessor variables)
	{
		return
		[
			new SendRequestActionDefinition(client, variables),
			new GetJsonValueActionDefinition(client, variables)
		];
	}
}
