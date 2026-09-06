using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Pairing;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Auth;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Api.Controllers;

public sealed record PluginPairingClientInfoBody(string? ExecutablePath, int? ProcessId, string? SdkVersion);

public sealed record PluginPairingRequestBody(
	string RequestId,
	string PluginId,
	string DisplayName,
	PluginPairingClientInfoBody? Client,
	DateTimeOffset CreatedAt,
	DateTimeOffset ExpiresAt,
	bool ReplacesExistingRegistration,
	string? ExistingRegistrationOrigin,
	DateTime? ExistingRegistrationCreatedAt,
	bool ArrivedOnPublicListener);

public sealed record GetPluginPairingRequestsResponse(IReadOnlyList<PluginPairingRequestBody> Requests);

public sealed record ApprovePluginPairingRequestBody(bool ReplaceExistingRegistration);

public sealed record PluginPairingActionResponse(bool Success, TransportError? Error);

public sealed record PluginPairedRegistrationBody(
	string PluginId,
	string DisplayName,
	DateTime CreatedAt,
	DateTime? LastSeenAt,
	bool Online);

public sealed record GetPluginPairedRegistrationsResponse(IReadOnlyList<PluginPairedRegistrationBody> Registrations);

/// <summary>
/// Desktop-only surface for approving, rejecting and managing interactive plugin pairing. No
/// <c>[Authorize]</c> attributes: the fallback authorization policy is already admin, and the loopback
/// authentication handler already authenticates the desktop UI's own requests without a bearer token.
///
/// Deliberately does NOT use <see cref="MacroDeckHost.Api.Plugins.PluginEndpointGate.TryReject" />, even
/// though every other plugin-facing controller does. <see cref="PluginBrowserGuard.IsBrowserRequest" />
/// treats the presence of an <c>Origin</c> header as a browser request, and the desktop UI is a real
/// browser that always sends <c>Origin</c> on unsafe methods - the gate would 403 the desktop UI's own
/// POSTs to this controller. (This is also why <c>DELETE /api/plugins/registration/{pluginId}</c> is
/// unreachable from the UI and has no caller today.) The loopback + trusted check below is this
/// controller's own, narrower substitute.
/// </summary>
[ApiController]
[Route("api/plugin-pairing")]
public class PluginPairingRequestsController : ControllerBase
{
	private readonly IPluginPairingService _pairingService;
	private readonly IPluginRegistrationService _registrationService;
	private readonly IMediator _mediator;

	public PluginPairingRequestsController(
		IPluginPairingService pairingService,
		IPluginRegistrationService registrationService,
		IMediator mediator)
	{
		_pairingService = pairingService;
		_registrationService = registrationService;
		_mediator = mediator;
	}

	[HttpGet("requests")]
	public async Task<IActionResult> GetRequests()
	{
		if (!TrustedDesktop(out var rejection))
		{
			return rejection!;
		}

		var pending = await _pairingService.Pending();

		return Ok(new GetPluginPairingRequestsResponse(pending.Select(item => new PluginPairingRequestBody(
				item.RequestId,
				item.PluginId,
				item.DisplayName,
				item.Client is { } client
					? new PluginPairingClientInfoBody(client.ExecutablePath, client.ProcessId, client.SdkVersion)
					: null,
				item.CreatedAt,
				item.ExpiresAt,
				item.ReplacesExistingRegistration,
				item.ExistingRegistrationOrigin,
				item.ExistingRegistrationCreatedAt,
				item.ArrivedOnPublicListener))
			.ToList()));
	}

	[HttpPost("requests/{requestId}/approve")]
	public async Task<IActionResult> Approve(string requestId,
		ApprovePluginPairingRequestBody body,
		CancellationToken cancellationToken)
	{
		if (!TrustedDesktop(out var rejection))
		{
			return rejection!;
		}

		var result = await _pairingService.Approve(requestId, body.ReplaceExistingRegistration);
		if (!result.Succeeded)
		{
			return Ok(new PluginPairingActionResponse(false,
				result.Error == PluginPairingApproveError.ReplacementNotConfirmed
					? new TransportError
					{
						Code = "replacement_not_confirmed",
						Message = AppStrings.Errors.Plugins.ReplaceCredentialConfirmationRequired()
					}
					: new TransportError
						{ Code = "not_found", Message = AppStrings.Errors.Plugins.PairingRequestNotFound() }));
		}

		await _mediator.Publish(new PluginPairingRequestsChangedNotification(), cancellationToken);

		return Ok(new PluginPairingActionResponse(true, null));
	}

	[HttpPost("requests/{requestId}/reject")]
	public async Task<IActionResult> Reject(string requestId, CancellationToken cancellationToken)
	{
		if (!TrustedDesktop(out var rejection))
		{
			return rejection!;
		}

		if (!_pairingService.Reject(requestId))
		{
			return Ok(new PluginPairingActionResponse(false,
				new TransportError
					{ Code = "not_found", Message = AppStrings.Errors.Plugins.PairingRequestNotFound() }));
		}

		await _mediator.Publish(new PluginPairingRequestsChangedNotification(), cancellationToken);

		return Ok(new PluginPairingActionResponse(true, null));
	}

	[HttpGet("registrations")]
	public async Task<IActionResult> GetRegistrations()
	{
		if (!TrustedDesktop(out var rejection))
		{
			return rejection!;
		}

		var registrations = await _pairingService.PairedRegistrations();

		return Ok(new GetPluginPairedRegistrationsResponse(registrations.Select(registration
				=> new PluginPairedRegistrationBody(registration.PluginId,
					registration.DisplayName,
					registration.CreatedAt,
					registration.LastSeenAt,
					registration.Online))
			.ToList()));
	}

	[HttpPost("registrations/{pluginId}/revoke")]
	public async Task<IActionResult> RevokeRegistration(string pluginId, CancellationToken cancellationToken)
	{
		if (!TrustedDesktop(out var rejection))
		{
			return rejection!;
		}

		await _registrationService.Revoke(pluginId);
		await _mediator.Publish(new PluginTokensChangedNotification(), cancellationToken);
		await _mediator.Publish(new PluginSessionsChangedNotification(), cancellationToken);
		await _mediator.Publish(new PluginPairingRequestsChangedNotification(), cancellationToken);

		return NoContent();
	}

	// Admin scope alone is deliberately insufficient: an admin bearer token obtained on the public
	// listener must never be able to approve a pairing request. Approval is meant to be a physical
	// "someone is at this machine, looking at this prompt" decision, not merely an authorization one -
	// exactly the same reasoning AuthController.Setup applies to initial account setup.
	private bool TrustedDesktop(out IActionResult? rejection)
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			rejection = Problem(statusCode: StatusCodes.Status403Forbidden,
				title: "Plugin pairing can only be approved from the desktop app.");
			return false;
		}

		rejection = null;
		return true;
	}
}
