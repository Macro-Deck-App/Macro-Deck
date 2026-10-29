using System.Security.Claims;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Infrastructure.Mcp;
using MacroDeckHost.Auth;
using MacroDeckHost.Mcp.Tools;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MacroDeckHost.Mcp;

public static class McpServerSetup
{
	public static void AddMacroDeckMcp(this IServiceCollection services)
	{
		services.AddHttpContextAccessor();
		services.AddSingleton<McpConnectionRegistry>();
		services.AddSingleton<IMcpConnections>(provider => provider.GetRequiredService<McpConnectionRegistry>());
		services.AddScoped<McpTextResolver>();
		services.AddScoped<McpConfirmationText>();
		services.AddScoped<McpConfirmationGate>();
		services.AddSingleton<IMcpSecretEntryStore, McpSecretEntryStore>();
		services.AddSingleton<IMcpConfirmationStore, McpConfirmationStore>();
		services.AddSingleton<IMcpExecutionRegistry, McpExecutionRegistry>();
		services.AddSingleton<IMcpProtectedReferences, McpProtectedReferences>();
		services.AddSingleton<IMcpVariableWriteGuard, McpVariableWriteGuard>();
		services.AddSingleton<IMcpStateStore, McpStateStore>();
		services.AddScoped<IMcpSecretHolders, McpSecretHolders>();
		services.AddScoped<IMcpSecretLedger, McpSecretLedger>();
		services.AddScoped<IMcpJournal, McpJournal>();
		services.AddScoped<IMcpEntityStore, McpEntityStore>();
		services.AddScoped<IMcpRiskPolicy, McpRiskPolicy>();
		services.AddSingleton<IMcpRunInspector, McpRunInspector>();
		services.AddScoped<IMcpChangeSet, McpChangeSet>();
		services.AddHostedService<McpDeferredScrubService>();
		services.AddScoped<SecretScrubDeferral>();

		services.AddMcpServer(options =>
			{
				options.ServerInfo = new Implementation
				{
					Name = McpContract.ServerName,
					Version = typeof(McpServerSetup).Assembly.GetName().Version?.ToString() ?? "0.0.0"
				};
			})
			.WithHttpTransport(options =>
			{
				// Clients before the 2026-07-28 revision need a session to receive elicitation requests;
				// later clients are stateless and get elicitation as multi round-trip results instead.
				options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients;
			})
			.WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
			{
				TrackClient(context.Services, context.Server, context.User);
				return await next(context, cancellationToken);
			}))
			.WithTools<McpCatalogTools>(McpJson.Options)
			.WithTools<McpEntityTools>(McpJson.Options)
			.WithTools<McpSecretTools>(McpJson.Options)
			.WithTools<McpWriteTools>(McpJson.Options)
			.WithTools<McpRunTools>(McpJson.Options);
	}

	private static void TrackClient(IServiceProvider? services, McpServer server, ClaimsPrincipal? user)
	{
		var registry = services?.GetService<McpConnectionRegistry>();
		if (registry is null || !Guid.TryParse(user?.FindFirst(McpClaimTypes.TokenId)?.Value, out var tokenId))
		{
			return;
		}

		var name = server.ClientInfo?.Name;
		var version = server.ClientInfo?.Version;
		if (server.SessionId is { } sessionId)
		{
			registry.Touch(sessionId, name, version);
		}
		else
		{
			registry.TouchStateless(tokenId, name, version);
		}
	}

	public static IApplicationBuilder UseMcpRequestGuard(this IApplicationBuilder app)
	{
		return app.UseWhen(context => McpPaths.IsMcpRequest(context.Request.Path),
			branch => branch.Use(async (context, next) =>
			{
				if (McpRequestGuard.Refusal(context) is { } status)
				{
					context.Response.StatusCode = status;
					return;
				}

				await next(context);
			}));
	}

	public static IApplicationBuilder UseMcpSessionBinding(this IApplicationBuilder app)
	{
		return app.UseWhen(context => McpPaths.IsTransportRequest(context.Request.Path),
			branch => branch.UseMiddleware<McpSessionBindingMiddleware>());
	}

	public static void MapMacroDeckMcp(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapMcp(McpPaths.Endpoint).RequireAuthorization(AuthPolicies.Mcp);
		endpoints.MapMcpSecretEntryPage();
		endpoints.MapMcpConfirmationPage();
	}
}
