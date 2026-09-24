namespace MacroDeckHost.Application.CompanionApp;

public sealed record CompanionAppRelease(
	string Version,
	int VersionCode,
	Uri Url,
	string Sha256,
	DateTimeOffset? PublishedAt);
