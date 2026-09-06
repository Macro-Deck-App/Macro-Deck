using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Endpoints;

/// <summary>
/// The endpoints the SDK serves under <see cref="ReservedPaths.Prefix" />.
///
/// <para>
/// Deliberately small and deliberately boring: liveness, readiness, identity and connection
/// diagnostics. Nothing here reads a secret or a session token, so the loopback-only default binding
/// is the whole of the access control - which is only true as long as it stays that way.
/// </para>
/// </summary>
internal static class MacroDeckEndpoints
{
	public static void Map(IEndpointRouteBuilder endpoints)
	{
		endpoints.MapGet(ReservedPaths.Health, () => Results.Ok(new { status = "healthy" }));

		endpoints.MapGet(ReservedPaths.Ready,
			(PluginConnectionState state) => state.IsReady
				? Results.Ok(new { status = "ready" })
				: Results.Json(new { status = "not-ready", state = state.Status.ToString() },
					statusCode: StatusCodes.Status503ServiceUnavailable));

		endpoints.MapGet(ReservedPaths.Info,
			(PluginMetadata metadata, PluginConnectionState state, IServiceProvider services) => Results.Ok(new
			{
				id = metadata.Id,
				name = metadata.Name,
				version = metadata.Version,
				description = metadata.Description,
				icon = metadata.IconPath,
				mode = services.GetRequiredService<PluginRegistrationModeAccessor>().Mode.ToString(),
				sdkVersion = typeof(MacroDeckPlugin).Assembly.GetName().Version?.ToString(),
				protocolVersion = state.NegotiatedVersion ?? ProtocolVersions.Current
			}));

		endpoints.MapGet(ReservedPaths.Diagnostics,
			(PluginConnectionState state) => Results.Ok(new
			{
				status = state.Status.ToString(),
				faultReason = state.FaultReason,
				sessionId = state.SessionId,
				negotiatedVersion = state.NegotiatedVersion,
				reconnectAttempt = state.ReconnectAttempt,
				lastCloseCode = state.LastCloseCode,
				inFlightInvocations = state.InFlightInvocations,
				declaredCapabilities = state.DeclaredCapabilities,
				acceptedCapabilities = state.AcceptedCapabilities
			}));
	}
}
