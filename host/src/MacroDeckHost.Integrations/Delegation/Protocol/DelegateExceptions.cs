namespace MacroDeckHost.Integrations.Delegation.Protocol;

internal abstract class DelegateClientException : Exception
{
	protected DelegateClientException(string message, Exception? inner = null)
		: base(message, inner)
	{
	}
}

internal sealed class DelegateUnreachableException : DelegateClientException
{
	public DelegateUnreachableException(string message = "The remote could not be reached.", Exception? inner = null)
		: base(message, inner)
	{
	}
}

internal sealed class DelegateTlsException : DelegateClientException
{
	public DelegateTlsException(string message = "The certificate presented by the remote could not be validated.",
		Exception? inner = null)
		: base(message, inner)
	{
	}
}

internal sealed class DelegateNotMacroDeckException : DelegateClientException
{
	public DelegateNotMacroDeckException(string message = "That address did not answer as a Macro Deck.",
		Exception? inner = null)
		: base(message, inner)
	{
	}
}

internal sealed class DelegateUnauthorizedException : DelegateClientException
{
	public DelegateUnauthorizedException(string message = "The credentials were rejected.")
		: base(message)
	{
	}
}

internal sealed class DelegateForbiddenException : DelegateClientException
{
	public DelegateForbiddenException(string message = "This account is not allowed to do that.")
		: base(message)
	{
	}
}

internal sealed class DelegateNotFoundException : DelegateClientException
{
	public DelegateNotFoundException(string message = "Not found.")
		: base(message)
	{
	}
}

internal sealed class DelegateRateLimitedException : DelegateClientException
{
	public DelegateRateLimitedException(TimeSpan retryAfter, string message = "Too many attempts.")
		: base(message)
		=> RetryAfter = retryAfter;

	public TimeSpan RetryAfter { get; }
}

internal sealed class DelegateServerErrorException : DelegateClientException
{
	public DelegateServerErrorException(string message = "The remote returned an unexpected error.",
		Exception? inner = null)
		: base(message, inner)
	{
	}
}

internal sealed class DelegateDepthExceededException : DelegateClientException
{
	public DelegateDepthExceededException(string message = "The delegate hop budget was exhausted.")
		: base(message)
	{
	}
}

internal sealed class DelegateCredentialsRejectedException : DelegateClientException
{
	public DelegateCredentialsRejectedException()
		: base("Sign-in was rejected and will not be retried automatically.")
	{
	}
}

internal sealed class DelegateThrottledException : DelegateClientException
{
	public DelegateThrottledException(TimeSpan remaining)
		: base("The remote is temporarily locked out.")
		=> Remaining = remaining;

	public TimeSpan Remaining { get; }
}
