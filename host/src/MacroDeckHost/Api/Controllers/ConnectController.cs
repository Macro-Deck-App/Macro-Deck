using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Connect;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/connect")]
public class ConnectController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetConnectSessionRequest, GetConnectSessionResponse> _getSession;

	private readonly IUiTransportMessageHandler<StartConnectSignInRequest, StartConnectSignInResponse>
		_startSignIn;

	private readonly IUiTransportMessageHandler<CancelConnectSignInRequest, CancelConnectSignInResponse>
		_cancelSignIn;

	private readonly IUiTransportMessageHandler<SignOutConnectRequest, SignOutConnectResponse> _signOut;
	private readonly IConnectAvatarCache _avatarCache;

	public ConnectController(
		IUiTransportMessageHandler<GetConnectSessionRequest, GetConnectSessionResponse> getSession,
		IUiTransportMessageHandler<StartConnectSignInRequest, StartConnectSignInResponse> startSignIn,
		IUiTransportMessageHandler<CancelConnectSignInRequest, CancelConnectSignInResponse> cancelSignIn,
		IUiTransportMessageHandler<SignOutConnectRequest, SignOutConnectResponse> signOut,
		IConnectAvatarCache avatarCache)
	{
		_getSession = getSession;
		_startSignIn = startSignIn;
		_cancelSignIn = cancelSignIn;
		_signOut = signOut;
		_avatarCache = avatarCache;
	}

	// The whole account surface is admin-only, reads included: the web client is deliberately unaware
	// that Macro Deck accounts exist. Loopback authenticates as admin, so the desktop app in its own
	// window and the desktop UI opened over the public listener are both covered by this one policy.
	[HttpGet("session")]
	[Authorize(Policy = AuthPolicies.Admin)]
	public Task<GetConnectSessionResponse> GetSession(CancellationToken ct)
		=> _getSession.Handle(new GetConnectSessionRequest(), ct).AsTask();

	[HttpGet("avatar")]
	[Authorize(Policy = AuthPolicies.Admin)]
	public async Task<IActionResult> GetAvatar(CancellationToken ct)
	{
		var stream = await _avatarCache.GetAvatar(ct);

		if (stream is not null)
		{
			return File(stream, "image/png");
		}

		// No avatar means no image at all - never a placeholder, never a 200 with an empty body. The length
		// is set explicitly so the status-code-pages middleware leaves the response body empty.
		Response.StatusCode = StatusCodes.Status404NotFound;
		Response.ContentLength = 0;

		return new EmptyResult();
	}

	// No loopback gate any more: the device code grant has no redirect URI, so there is nothing left
	// that only resolves on the host machine (issue #673).
	[HttpPost("signin/start")]
	[Authorize(Policy = AuthPolicies.Admin)]
	public Task<StartConnectSignInResponse> StartSignIn(CancellationToken ct)
		=> _startSignIn.Handle(new StartConnectSignInRequest(), ct).AsTask();

	[HttpPost("signin/cancel")]
	[Authorize(Policy = AuthPolicies.Admin)]
	public Task<CancelConnectSignInResponse> CancelSignIn(CancellationToken ct)
		=> _cancelSignIn.Handle(new CancelConnectSignInRequest(), ct).AsTask();

	[HttpPost("signout")]
	[Authorize(Policy = AuthPolicies.Admin)]
	public Task<SignOutConnectResponse> SignOut(CancellationToken ct)
		=> _signOut.Handle(new SignOutConnectRequest(), ct).AsTask();
}
