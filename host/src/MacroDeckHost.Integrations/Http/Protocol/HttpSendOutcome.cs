namespace MacroDeckHost.Integrations.Http.Protocol;

internal enum HttpFailureKind
{
	Timeout,
	Unreachable,
	TlsRejected,
	FileMissing,
	FileUnreadable
}

internal sealed record HttpSendOutcome
{
	public HttpResponseSnapshot? Response { get; private init; }

	public HttpFailureKind? Failure { get; private init; }

	public long DurationMs { get; private init; }

	public static HttpSendOutcome Succeeded(HttpResponseSnapshot response)
	{
		return new HttpSendOutcome { Response = response, DurationMs = response.DurationMs };
	}

	public static HttpSendOutcome Failed(HttpFailureKind failure, long durationMs)
	{
		return new HttpSendOutcome { Failure = failure, DurationMs = durationMs };
	}
}
