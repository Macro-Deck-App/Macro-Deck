namespace MacroDeckHost.Application.Connect;

/// <summary>
/// A retryable failure talking to Macro Deck Connect: a 429, a 5xx, a malformed/empty body, or a
/// network-level failure. The session must stay signed in through this - only a definitive server
/// repudiation or an explicit sign-out may end it.
/// </summary>
public class ConnectAuthTransientException : Exception
{
	public ConnectAuthTransientException(string message, Exception? innerException = null)
		: base(message, innerException)
	{
	}

	public TimeSpan? RetryAfter { get; init; }

	public bool IsRateLimit { get; init; }
}

/// <summary>
/// The credential is definitively no longer usable: <c>invalid_grant</c> from the token endpoint
/// (revoked, reused, expired, or the account's security stamp rotated), or the credential could not
/// be resolved locally (a lost/rotated Data Protection key). The credential must be dropped and
/// interactive sign-in required.
/// </summary>
public class ConnectAuthRejectedException : Exception
{
	public ConnectAuthRejectedException(string message, Exception? innerException = null)
		: base(message, innerException)
	{
	}
}

/// <summary>
/// The token endpoint answered <c>access_denied</c> for a suspended account. This does not revoke the
/// authorization, so the credential must be kept and refreshing must stop until the suspension is
/// lifted and retried.
/// </summary>
public class ConnectAccountSuspendedException : Exception
{
	public ConnectAccountSuspendedException(string message)
		: base(message)
	{
	}
}
