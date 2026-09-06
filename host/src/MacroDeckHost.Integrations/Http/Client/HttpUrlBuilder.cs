using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace MacroDeckHost.Integrations.Http.Client;

internal static class HttpUrlBuilder
{
	public static bool TryBuild(
		string? url,
		IReadOnlyDictionary<string, string>? query,
		[NotNullWhen(true)] out Uri? uri)
	{
		uri = null;

		if (string.IsNullOrWhiteSpace(url) ||
			!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
			!IsHttpOrHttps(parsed))
		{
			return false;
		}

		if (query is null || query.Count == 0)
		{
			uri = parsed;
			return true;
		}

		var builder = new UriBuilder(parsed);
		var combined = new StringBuilder(builder.Query.TrimStart('?'));
		foreach (var (key, value) in query)
		{
			if (string.IsNullOrEmpty(key))
			{
				continue;
			}

			if (combined.Length > 0)
			{
				combined.Append('&');
			}

			combined.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
		}

		builder.Query = combined.ToString();
		uri = builder.Uri;
		return true;
	}

	private static bool IsHttpOrHttps(Uri uri)
		=> string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ||
			string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);
}
