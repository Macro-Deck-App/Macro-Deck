namespace MacroDeckHost.Integrations.Http.Client;

internal sealed record HttpResponseSnapshot(
	int StatusCode,
	IReadOnlyDictionary<string, string> Headers,
	string Body,
	bool BodyTruncated,
	string? ContentType,
	long DurationMs);
