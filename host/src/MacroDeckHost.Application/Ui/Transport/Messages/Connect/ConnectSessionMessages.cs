namespace MacroDeckHost.Application.Ui.Transport.Messages.Connect;

public sealed class GetConnectSessionRequest;

public sealed class ConnectAccountPayload
{
	public string Subject { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	public bool AvatarAvailable { get; set; }

	/// <summary>
	/// Opaque token identifying the current picture. The avatar is served from one fixed host URL, so
	/// without this a changed picture would keep resolving to the cached image.
	/// </summary>
	public string? AvatarVersion { get; set; }

	public string? CreatorUsername { get; set; }

	public IReadOnlyList<string> Roles { get; set; } = [];
}

public sealed class GetConnectSessionResponse
{
	public string Status { get; set; } = string.Empty;

	public string Connectivity { get; set; } = string.Empty;

	public ConnectAccountPayload? Account { get; set; }

	public DateTimeOffset? OfflineSince { get; set; }

	public DateTimeOffset? LastSuccessfulRefreshUtc { get; set; }

	public string? Message { get; set; }

	/// <summary>Why the last sign-in attempt ended without a session, or null. A closed set of values the
	/// client renders in its own language, never text.</summary>
	public string? SignInFailure { get; set; }

	public string AccountManagementUrl { get; set; } = string.Empty;
}

public sealed class StartConnectSignInRequest;

public sealed class StartConnectSignInResponse
{
	/// <summary>Opened by the client. Already carries <see cref="UserCode"/>, so nothing has to be
	/// typed in the normal case.</summary>
	public string VerificationUriComplete { get; set; } = string.Empty;

	/// <summary>Shown so the page can also be reached by hand, from a device that cannot follow the
	/// link.</summary>
	public string VerificationUri { get; set; } = string.Empty;

	public string UserCode { get; set; } = string.Empty;

	public DateTimeOffset ExpiresAtUtc { get; set; }
}

public sealed class CancelConnectSignInRequest;

public sealed class CancelConnectSignInResponse;

public sealed class SignOutConnectRequest;

public sealed class SignOutConnectResponse;

/// <summary>Deliberately field-free: clients react to this by re-fetching <c>GET session</c> rather
/// than trusting anything the notification itself might carry.</summary>
public sealed class ConnectSessionChangedNotification;
