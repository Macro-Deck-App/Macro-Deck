using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Security;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

/// <summary>
/// Its own route prefix so the locked-host gate can allow it through with a single path check.
/// </summary>
[ApiController]
[Route("api/key-ring")]
public class KeyRingController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetKeyRingStatusRequest, GetKeyRingStatusResponse> _status;
	private readonly IUiTransportMessageHandler<UnlockKeyRingRequest, UnlockKeyRingResponse> _unlock;

	private readonly IUiTransportMessageHandler<GetKeyRingProtectionRequest, GetKeyRingProtectionResponse>
		_protection;

	public KeyRingController(IUiTransportMessageHandler<GetKeyRingStatusRequest, GetKeyRingStatusResponse> status,
		IUiTransportMessageHandler<UnlockKeyRingRequest, UnlockKeyRingResponse> unlock,
		IUiTransportMessageHandler<GetKeyRingProtectionRequest, GetKeyRingProtectionResponse> protection)
	{
		_status = status;
		_unlock = unlock;
		_protection = protection;
	}

	/// <summary>
	/// Anonymous on purpose: while the ring is locked the token signing key is unreadable, so an
	/// authenticated endpoint would be unreachable in exactly the state it exists to report.
	/// </summary>
	[HttpGet("status")]
	[AllowAnonymous]
	public async Task<GetKeyRingStatusResponse> Status(CancellationToken ct)
		=> await _status.Handle(new GetKeyRingStatusRequest(), ct);

	/// <summary>
	/// Takes a recovery key in the clear, so it is loopback-only and answers 404 elsewhere - the same
	/// shape the reveal and regenerate endpoints use. Anonymous because a locked host cannot issue or
	/// validate a token.
	/// </summary>
	[HttpPost("unlock")]
	[AllowAnonymous]
	public async Task<IActionResult> Unlock(UnlockKeyRingRequest body, CancellationToken ct)
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			return NotFound();
		}

		return Ok(await _unlock.Handle(body, ct));
	}

	/// <summary>
	/// The full picture for the settings screen, behind the normal admin policy. Reports which backend
	/// is in use and, when the ring is not protected, why - so the fallback is visible rather than
	/// something the user has to infer.
	/// </summary>
	[HttpGet("protection")]
	public async Task<GetKeyRingProtectionResponse> Protection(CancellationToken ct)
		=> await _protection.Handle(new GetKeyRingProtectionRequest(), ct);
}
