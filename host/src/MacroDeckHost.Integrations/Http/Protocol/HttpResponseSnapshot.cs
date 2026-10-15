namespace MacroDeckHost.Integrations.Http.Protocol;

internal sealed record HttpResponseSnapshot(
	int StatusCode,
	IReadOnlyDictionary<string, string> Headers,
	string Body,
	bool BodyTruncated,
	string? ContentType,
	long DurationMs);
