using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MacroDeckHost.Mcp;

// Elicitation is written as a multi round-trip request: 2026-07-28 clients answer it natively, and the
// SDK resolves it over a session for older clients, re-running the tool with the answer either way.
public static class McpElicitation
{
	public static ElicitResult? Answer(RequestContext<CallToolRequestParams> context, string key)
	{
		if (context.Params?.InputResponses is not { } responses || !responses.TryGetValue(key, out var response))
		{
			return null;
		}

		return response.RawValue.Deserialize<ElicitResult>(McpJsonUtilities.DefaultOptions);
	}

	public static InputRequiredException Ask(string key, ElicitRequestParams request, string state)
	{
		return new InputRequiredException(new Dictionary<string, InputRequest>
			{
				[key] = InputRequest.ForElicitation(request)
			},
			state);
	}
}
