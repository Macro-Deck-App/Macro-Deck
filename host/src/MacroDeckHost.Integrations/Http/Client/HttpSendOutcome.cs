namespace MacroDeckHost.Integrations.Http.Client;

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
		=> new() { Response = response, DurationMs = response.DurationMs };

	public static HttpSendOutcome Failed(HttpFailureKind failure, long durationMs)
		=> new() { Failure = failure, DurationMs = durationMs };
}
