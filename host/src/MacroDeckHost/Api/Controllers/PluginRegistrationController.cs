using MacroDeck.Plugin.Protocol;
using MacroDeck.Plugin.Protocol.Auth;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeckHost.Api.Plugins;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Services;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route(ProtocolConstants.RegistrationPath)]
public class PluginRegistrationController : ControllerBase
{
	private readonly IPluginTokenService _tokenService;
	private readonly IPluginRegistrationService _registrationService;
	private readonly LoginThrottle _throttle;
	private readonly IMediator _mediator;
	private readonly IAppPreferenceService _preferences;

	public PluginRegistrationController(
		IPluginTokenService tokenService,
		IPluginRegistrationService registrationService,
		[FromKeyedServices("plugin")] LoginThrottle throttle,
		IMediator mediator,
		IAppPreferenceService preferences)
	{
		_tokenService = tokenService;
		_registrationService = registrationService;
		_throttle = throttle;
		_mediator = mediator;
		_preferences = preferences;
	}

	[HttpPost]
	[AllowAnonymous]
	public async Task<IActionResult> Register(CancellationToken cancellationToken)
	{
		if (PluginEndpointGate.TryReject(HttpContext, out var rejection))
		{
			return rejection!;
		}

		var body = await PluginProtocolHttp.ReadBodyAsync<PluginRegistrationRequest>(Request, cancellationToken);
		if (body is null || string.IsNullOrEmpty(body.PluginId) || string.IsNullOrEmpty(body.DisplayName))
		{
			return PluginProtocolHttp.Error(PluginErrors.InvalidPayload(), StatusCodes.Status400BadRequest);
		}

		// Ahead of the throttle and deliberately not registering a failure: this refusal says nothing
		// about whether the caller's token is right, and charging it to the shared enrolment bucket
		// would let a plugin retrying against a switched-off host lock every other plugin out. Read per
		// request so flipping the switch takes effect without a restart.
		if (!(await _preferences.GetDeveloper()).Enabled)
		{
			return PluginProtocolHttp.Error(PluginErrors.DeveloperModeDisabled(), StatusCodes.Status403Forbidden);
		}

		// One shared bucket, deliberately not keyed by plugin id: the credential being guessed here is
		// the enrollment token, and a per-plugin-id key would let a caller vary the body to sidestep the
		// limit entirely. Enrolment is a rare, human-initiated action, so a shared lockout costs far
		// less than leaving token guessing unthrottled.
		const string throttleKey = "plugin-enroll";
		if (PluginEndpointGate.TryThrottle(_throttle, throttleKey, Response, out var throttled))
		{
			return throttled!;
		}

		var enrollmentToken = Request.Headers[PluginAuthDefaults.EnrollmentTokenHeaderName].ToString();
		var token = await _tokenService.Authenticate(enrollmentToken);
		if (token is null)
		{
			_throttle.RegisterFailure(throttleKey);
			return PluginProtocolHttp.Error(PluginErrors.Unauthenticated(), StatusCodes.Status401Unauthorized);
		}

		var result = await _registrationService.Register(body.PluginId,
			body.DisplayName,
			token.Id,
			PluginRegistrationOrigins.DeveloperToken);
		if (!result.Succeeded)
		{
			return result.Error == PluginRegistrationError.InvalidPluginId
				? PluginProtocolHttp.Error(PluginErrors.InvalidPayload(result.ErrorDetail is { } detail
						? new Dictionary<string, string> { ["pluginId"] = detail }
						: null),
					StatusCodes.Status400BadRequest)
				: PluginProtocolHttp.Error(PluginErrors.AlreadyRegistered(), StatusCodes.Status409Conflict);
		}

		_throttle.RegisterSuccess(throttleKey);
		await _mediator.Publish(new PluginTokensChangedNotification(), cancellationToken);

		return PluginProtocolHttp.Json(new PluginRegistrationResponse
				{ PluginId = result.Registration!.PluginId, PluginSecret = result.PluginSecret! },
			StatusCodes.Status201Created);
	}

	[HttpDelete("{pluginId}")]
	[AllowAnonymous]
	public async Task<IActionResult> Revoke(string pluginId, CancellationToken cancellationToken)
	{
		if (PluginEndpointGate.TryReject(HttpContext, out var rejection))
		{
			return rejection!;
		}

		var authResult = await HttpContext.AuthenticateAsync(AuthDefaults.PolicySchemeName);
		if (!authResult.Succeeded ||
			!string.Equals(authResult.Principal?.FindFirst(AuthDefaults.ScopeClaim)?.Value,
				AuthDefaults.AdminScope,
				StringComparison.Ordinal))
		{
			return PluginProtocolHttp.Error(PluginErrors.Unauthenticated(), StatusCodes.Status401Unauthorized);
		}

		await _registrationService.Revoke(pluginId);
		await _mediator.Publish(new PluginTokensChangedNotification(), cancellationToken);
		await _mediator.Publish(new PluginSessionsChangedNotification(), cancellationToken);

		return NoContent();
	}
}
