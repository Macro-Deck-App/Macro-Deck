using System.Security.Claims;
using MacroDeckHost.Application.Mcp;

namespace MacroDeckHost.Mcp;

public sealed class McpSessionBindingMiddleware
{
	private readonly RequestDelegate _next;
	private readonly McpConnectionRegistry _registry;

	public McpSessionBindingMiddleware(RequestDelegate next, McpConnectionRegistry registry)
	{
		_next = next;
		_registry = registry;
	}

	public async Task InvokeAsync(HttpContext context)
	{
		if (!McpPaths.IsTransportRequest(context.Request.Path) || TokenId(context.User) is not { } tokenId)
		{
			await _next(context);
			return;
		}

		// A session id is not a credential: a session created by one token must never be driven by another.
		var sessionId = context.Request.Headers[McpPaths.SessionHeader].ToString();
		if (sessionId.Length > 0 && _registry.IsBoundToOtherToken(sessionId, tokenId))
		{
			context.Response.StatusCode = StatusCodes.Status403Forbidden;
			return;
		}

		if (sessionId.Length == 0)
		{
			context.Response.OnStarting(() =>
			{
				var created = context.Response.Headers[McpPaths.SessionHeader].ToString();
				if (created.Length > 0)
				{
					_registry.Bind(created, tokenId);
				}
				else
				{
					_registry.TouchStateless(tokenId, null, null);
				}

				return Task.CompletedTask;
			});
		}
		else
		{
			_registry.Touch(sessionId, null, null);
		}

		_registry.BeginRequest(context, tokenId);
		try
		{
			await _next(context);
		}
		finally
		{
			_registry.EndRequest(context);
			if (sessionId.Length > 0 && HttpMethods.IsDelete(context.Request.Method))
			{
				_registry.Remove(sessionId);
			}
		}
	}

	private static Guid? TokenId(ClaimsPrincipal user)
	{
		return Guid.TryParse(user.FindFirst(McpClaimTypes.TokenId)?.Value, out var id) ? id : null;
	}
}
