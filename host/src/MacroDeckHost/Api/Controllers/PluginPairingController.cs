using System.Globalization;
using MacroDeck.Plugin.Protocol;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeckHost.Api.Plugins;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Pairing;
using MacroDeckHost.Auth;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route(ProtocolConstants.PairingPath)]
public class PluginPairingController : ControllerBase
{
	private readonly IPluginPairingService _pairingService;
	private readonly PluginPairingOptions _options;
	private readonly LoginThrottle _throttle;
	private readonly IMediator _mediator;

	public PluginPairingController(
		IPluginPairingService pairingService,
		PluginPairingOptions options,
		[FromKeyedServices("plugin")] LoginThrottle throttle,
		IMediator mediator)
	{
		_pairingService = pairingService;
		_options = options;
		_throttle = throttle;
		_mediator = mediator;
	}

	[HttpPost]
	[AllowAnonymous]
	public async Task<IActionResult> Create(CancellationToken cancellationToken)
	{
		if (PluginEndpointGate.TryReject(HttpContext, out var rejection))
		{
			return rejection!;
		}

		var body = await PluginProtocolHttp.ReadBodyAsync<PluginPairingRequest>(Request, cancellationToken);
		if (body is null ||
			string.IsNullOrEmpty(body.PluginId) ||
			string.IsNullOrEmpty(body.DisplayName) ||
			string.IsNullOrEmpty(body.CodeChallenge) ||
			string.IsNullOrEmpty(body.CodeChallengeMethod))
		{
			return PluginProtocolHttp.Error(PluginErrors.InvalidPayload(), StatusCodes.Status400BadRequest);
		}

		// One shared bucket, deliberately its own key rather than "plugin-enroll": pairing spam must not
		// be able to lock out real Developer-token enrollment, or vice versa. Not keyed by plugin id
		// either, for the same reason the enrollment bucket isn't - a caller could otherwise vary the
		// body on every attempt to sidestep the limit entirely.
		const string throttleKey = "plugin-pairing";
		if (PluginEndpointGate.TryThrottle(_throttle, throttleKey, Response, out var throttled))
		{
			return throttled!;
		}

		// Recorded so an approval prompt can warn when the request did not originate from the trusted
		// loopback listener - the pairing endpoint itself is reachable from any local process, loopback
		// or public, and the user deciding whether to trust it should know which.
		var arrivedOnPublicListener = !LoopbackConnection.IsTrusted(HttpContext);

		var result = await _pairingService.Create(body.PluginId,
			body.DisplayName,
			body.CodeChallenge,
			body.CodeChallengeMethod,
			body.Client,
			arrivedOnPublicListener);

		if (!result.Succeeded)
		{
			_throttle.RegisterFailure(throttleKey);

			return result.Error switch
			{
				PluginPairingCreateError.DeveloperModeDisabled
					=> PluginProtocolHttp.Error(PluginErrors.Forbidden(), StatusCodes.Status403Forbidden),
				PluginPairingCreateError.InvalidPayload => PluginProtocolHttp.Error(PluginErrors.InvalidPayload(
						result.ErrorDetail is { } detail
							? new Dictionary<string, string> { ["pluginId"] = detail }
							: null),
					StatusCodes.Status400BadRequest),
				PluginPairingCreateError.AlreadyRegistered
					=> PluginProtocolHttp.Error(PluginErrors.AlreadyRegistered(), StatusCodes.Status409Conflict),
				_ => RateLimited()
			};
		}

		_throttle.RegisterSuccess(throttleKey);

		var record = result.Record!;
		await _mediator.Publish(new PluginPairingRequestsChangedNotification(), cancellationToken);
		await _mediator.Publish(new PluginPairingRequestedNotification(record.PluginId, record.DisplayName),
			cancellationToken);

		return PluginProtocolHttp.Json(new PluginPairingResponse
			{
				RequestId = record.RequestId,
				ExpiresAt = record.ExpiresAt,
				PollIntervalSeconds = (int)_options.PollInterval.TotalSeconds
			},
			StatusCodes.Status201Created);

		IActionResult RateLimited()
		{
			var retryAfter = _options.PollInterval;
			Response.Headers.RetryAfter
				= ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
			return PluginProtocolHttp.Error(PluginErrors.RateLimited(retryAfter), StatusCodes.Status429TooManyRequests);
		}
	}

	[HttpGet("{requestId}")]
	[AllowAnonymous]
	public IActionResult Status(string requestId)
	{
		if (PluginEndpointGate.TryReject(HttpContext, out var rejection))
		{
			return rejection!;
		}

		// No throttle accounting here - status polling is expected, routine traffic, not a guess.
		var status = _pairingService.Status(requestId);

		return PluginProtocolHttp.Json(new PluginPairingStatusResponse
			{
				Status = status.Status,
				ExpiresAt = status.ExpiresAt
			},
			StatusCodes.Status200OK);
	}

	[HttpPost("{requestId}/redemption")]
	[AllowAnonymous]
	public async Task<IActionResult> Redeem(string requestId, CancellationToken cancellationToken)
	{
		if (PluginEndpointGate.TryReject(HttpContext, out var rejection))
		{
			return rejection!;
		}

		var body = await PluginProtocolHttp.ReadBodyAsync<PluginPairingRedemptionRequest>(Request, cancellationToken);
		if (body is null || string.IsNullOrEmpty(body.CodeVerifier))
		{
			return PluginProtocolHttp.Error(PluginErrors.InvalidPayload(), StatusCodes.Status400BadRequest);
		}

		const string throttleKey = "plugin-pairing-redeem";
		if (PluginEndpointGate.TryThrottle(_throttle, throttleKey, Response, out var throttled))
		{
			return throttled!;
		}

		var result = await _pairingService.Redeem(requestId, body.CodeVerifier);
		if (!result.Succeeded)
		{
			_throttle.RegisterFailure(throttleKey);

			// Every redemption failure - unknown request id, expired, not approved, already redeemed, or
			// a wrong verifier - answers 401 identically. The status endpoint is where a plugin learns
			// where it stands; this endpoint must not become a second, more precise oracle for the same
			// state, or a verifier-guessing attempt would get a free signal on every guess.
			return PluginProtocolHttp.Error(PluginErrors.Unauthenticated(), StatusCodes.Status401Unauthorized);
		}

		_throttle.RegisterSuccess(throttleKey);
		await _mediator.Publish(new PluginPairingRequestsChangedNotification(), cancellationToken);

		return PluginProtocolHttp.Json(new PluginRegistrationResponse
			{
				PluginId = result.PluginId!,
				PluginSecret = result.PluginSecret!
			},
			StatusCodes.Status201Created);
	}
}
