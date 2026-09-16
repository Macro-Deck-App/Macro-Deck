using MacroDeckHost.Application.Configuration;

namespace MacroDeckHost.Application.Store.Reviews;

public sealed record StorePlatformOptions
{
	public const string BaseUrlEnvironmentVariable = "MACRO_DECK_PLATFORM_URL";

	public static readonly Uri DefaultBaseUrl = new("https://api.macro-deck.app/");

	public static readonly StorePlatformOptions Default = new();

	public const int MaxIdsPerRequest = 100;

	public Uri BaseUrl { get; init; } = DefaultBaseUrl;

	public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

	public TimeSpan RatingsCacheLifetime { get; init; } = TimeSpan.FromSeconds(60);

	// The Connect bearer token follows this URL, so only a development build may redirect it, and only
	// to https or to this machine.
	public static StorePlatformOptions Resolve(BuildChannel channel, string? overrideUrl)
	{
		if (channel != BuildChannel.Development ||
			string.IsNullOrWhiteSpace(overrideUrl) ||
			!Uri.TryCreate(overrideUrl.Trim(), UriKind.Absolute, out var uri) ||
			uri.UserInfo.Length > 0)
		{
			return Default;
		}

		var allowed = uri.Scheme == Uri.UriSchemeHttps ||
			(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
		if (!allowed)
		{
			return Default;
		}

		var normalized = uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
		return Default with { BaseUrl = normalized };
	}
}
