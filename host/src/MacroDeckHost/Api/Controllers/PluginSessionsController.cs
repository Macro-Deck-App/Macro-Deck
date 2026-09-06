using System.Net.WebSockets;
using MacroDeck.Plugin.Protocol;
using MacroDeck.Plugin.Protocol.Auth;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Api.Plugins;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Auth;
using MacroDeckHost.Plugins;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route(ProtocolConstants.SessionsPath)]
public class PluginSessionsController : ControllerBase
{
	private readonly IPluginLaunchTokenService _launchTokenService;
	private readonly IPluginRegistrationService _registrationService;
	private readonly IPluginSessionService _sessionService;
	private readonly LoginThrottle _throttle;
	private readonly IMediator _mediator;
	private readonly IAppPreferenceService _preferences;
	private readonly ILogger _logger;

	public PluginSessionsController(
		IPluginLaunchTokenService launchTokenService,
		IPluginRegistrationService registrationService,
		IPluginSessionService sessionService,
		[FromKeyedServices("plugin")] LoginThrottle throttle,
		IMediator mediator,
		IAppPreferenceService preferences,
		ILogger logger)
	{
		_launchTokenService = launchTokenService;
		_registrationService = registrationService;
		_sessionService = sessionService;
		_throttle = throttle;
		_mediator = mediator;
		_preferences = preferences;
		_logger = logger.ForContext<PluginSessionsController>();
	}

	[HttpPost]
	[AllowAnonymous]
	public async Task<IActionResult> CreateSession(CancellationToken cancellationToken)
	{
		if (PluginEndpointGate.TryReject(HttpContext, out var rejection))
		{
			return rejection!;
		}

		var pluginId = Request.Headers[PluginAuthDefaults.PluginIdHeaderName].ToString();
		var secret = Request.Headers[PluginAuthDefaults.PluginSecretHeaderName].ToString();
		if (string.IsNullOrEmpty(pluginId))
		{
			return PluginProtocolHttp.Error(PluginErrors.Unauthenticated(), StatusCodes.Status401Unauthorized);
		}

		var throttleKey = $"plugin-session|{pluginId}";
		if (PluginEndpointGate.TryThrottle(_throttle, throttleKey, Response, out var throttled))
		{
			return throttled!;
		}

		var body = await PluginProtocolHttp.ReadBodyAsync<PluginSessionRequest>(Request, cancellationToken);
		if (body is null)
		{
			return PluginProtocolHttp.Error(PluginErrors.InvalidPayload(), StatusCodes.Status400BadRequest);
		}

		string? launchId = null;
		PluginSessionIdentity? identity = null;

		if (_launchTokenService.TryAcquire(pluginId, secret, out var launch))
		{
			launchId = launch!.LaunchId;
			identity = new PluginSessionIdentity
			{
				PluginId = launch.PluginId,
				DisplayName = launch.DisplayName,
				Origin = PluginSessionOrigin.Managed
			};
		}
		else
		{
			var registration = await _registrationService.Authenticate(pluginId, secret);
			if (registration is not null)
			{
				// Every registration comes from a developer token or interactive pairing, so reaching
				// this branch means the caller holds a development credential. Refusing here rather than
				// only at enrolment is what makes Developer Mode a kill switch: the plugin already has a
				// secret and its reconnect loop would otherwise walk straight back in. A plugin Macro
				// Deck launched itself authenticates with a launch token above and never gets here.
				if (!(await _preferences.GetDeveloper()).Enabled)
				{
					// No throttle failure: the credential was valid, the switch was not on.
					return PluginProtocolHttp.Error(PluginErrors.DeveloperModeDisabled(),
						StatusCodes.Status403Forbidden);
				}

				if (_launchTokenService.HasActiveLaunch(pluginId))
				{
					PluginWebSocketLog.RegistrationSessionRefusedForLiveLaunch(_logger, pluginId);
				}
				else
				{
					identity = new PluginSessionIdentity
					{
						PluginId = registration.PluginId,
						DisplayName = registration.DisplayName,
						AccessTokenId = registration.AccessTokenId,
						Origin = PluginSessionOrigin.SelfRegistered
					};
				}
			}
		}

		if (identity is null)
		{
			_throttle.RegisterFailure(throttleKey);
			return PluginProtocolHttp.Error(PluginErrors.Unauthenticated(), StatusCodes.Status401Unauthorized);
		}

		var result = await _sessionService.Create(identity, body);
		if (!result.Succeeded)
		{
			if (launchId is not null)
			{
				_launchTokenService.Release(launchId);
			}

			if (result.Error == PluginSessionCreationError.VersionUnsupported)
			{
				return PluginProtocolHttp.Error(PluginErrors.VersionUnsupported(result.HostVersionRange!),
					StatusCodes.Status422UnprocessableEntity);
			}

			var (field, detail) = result.Error switch
			{
				PluginSessionCreationError.DeclaredVersionTooLong => ("declaredVersion",
					$"The declared version must be at most {ProtocolLimits.MaxDeclaredVersionLength} characters."),
				PluginSessionCreationError.SdkVersionTooLong => ("sdk.sdkVersion",
					$"The SDK version must be at most {ProtocolLimits.MaxSdkVersionLength} characters."),
				PluginSessionCreationError.TooManyReportedDeprecatedApis => ("sdk.deprecatedApis",
					$"At most {ProtocolLimits.MaxReportedDeprecatedApis} deprecated APIs may be reported."),
				PluginSessionCreationError.DeprecatedApiIdTooLong => ("sdk.deprecatedApis",
					$"Each reported API id must be at most {ProtocolLimits.MaxDeprecatedApiIdLength} characters."),
				PluginSessionCreationError.InvalidDeclaredName => ("declaredName",
					$"The declared name must be at most {ProtocolLimits.MaxDeclaredNameLength} characters " +
					"and must not contain control characters."),
				_ => ("capabilities", "Too many declared capabilities.")
			};

			return PluginProtocolHttp.Error(
				PluginErrors.InvalidPayload(new Dictionary<string, string> { [field] = detail }),
				StatusCodes.Status400BadRequest);
		}

		_throttle.RegisterSuccess(throttleKey);

		if (launchId is not null)
		{
			_launchTokenService.Bind(launchId, result.Response!.SessionId);
		}

		await _mediator.Publish(new PluginSessionsChangedNotification(), cancellationToken);
		await _mediator.Publish(new PluginCompatibilityChangedNotification(), cancellationToken);

		return PluginProtocolHttp.Json(result.Response, StatusCodes.Status201Created);
	}

	[HttpDelete("{sessionId}")]
	[AllowAnonymous]
	public async Task<IActionResult> CloseSession(string sessionId, CancellationToken cancellationToken)
	{
		if (PluginEndpointGate.TryReject(HttpContext, out var rejection))
		{
			return rejection!;
		}

		var authResult = await HttpContext.AuthenticateAsync(PluginAuthSchemes.PluginSession);
		if (!authResult.Succeeded || authResult.Principal is null)
		{
			return PluginProtocolHttp.Error(PluginErrors.Unauthenticated(), StatusCodes.Status401Unauthorized);
		}

		var authenticatedSessionId = authResult.Principal.FindFirst(PluginClaimTypes.SessionId)?.Value;
		if (!string.Equals(authenticatedSessionId, sessionId, StringComparison.Ordinal))
		{
			// A valid credential for a different session, not the absence of one - forbidden, not
			// unauthenticated: one plugin must not be able to close another's session.
			return PluginProtocolHttp.Error(PluginErrors.Forbidden(), StatusCodes.Status403Forbidden);
		}

		await _sessionService.Close(sessionId,
			(int)WebSocketCloseStatus.NormalClosure,
			"Closed by the plugin.");

		await _mediator.Publish(new PluginSessionsChangedNotification(), cancellationToken);

		return NoContent();
	}
}
