using System.Security.Cryptography;

namespace MacroDeckHost.Infrastructure.Store;

internal sealed record StoreFetch
{
	public required bool Success { get; init; }

	public byte[]? Content { get; init; }

	public string? Sha256 { get; init; }

	public string? FailureMessage { get; init; }

	public bool SizeMismatch { get; init; }

	public static StoreFetch Ok(byte[] content) => new()
	{
		Success = true,
		Content = content,
		Sha256 = Convert.ToHexStringLower(SHA256.HashData(content))
	};

	public static StoreFetch Fail(string message, bool sizeMismatch = false) => new()
	{
		Success = false,
		FailureMessage = message,
		SizeMismatch = sizeMismatch
	};
}

internal static class StoreHttp
{
	public const string RegistryClientName = "store-registry";

	public const string ArtifactClientName = "store-artifact";

	// The registry and the asset CDN are both https today. A plain-http URL anywhere in registry data
	// is treated as malformed rather than followed, so a downgrade cannot be introduced by content.
	public static bool IsHttps(Uri uri) =>
		uri.IsAbsoluteUri && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);

	public static async Task<StoreFetch> Fetch(HttpClient client,
		Uri url,
		long maxBytes,
		long? expectedSize,
		CancellationToken cancellationToken)
	{
		if (!IsHttps(url))
		{
			return StoreFetch.Fail($"'{url}' is not an https URL.");
		}

		if (expectedSize is > 0 && expectedSize > maxBytes)
		{
			return StoreFetch.Fail($"'{url}' declares {expectedSize} bytes, above the {maxBytes} byte limit.",
				sizeMismatch: true);
		}

		HttpResponseMessage response;
		try
		{
			response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
		{
			return StoreFetch.Fail($"'{url}' could not be fetched: {ex.Message}");
		}

		using (response)
		{
			if (!response.IsSuccessStatusCode)
			{
				return StoreFetch.Fail($"'{url}' answered {(int)response.StatusCode}.");
			}

			var limit = expectedSize is > 0 ? Math.Min(expectedSize.Value, maxBytes) : maxBytes;
			await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
			using var buffer = new MemoryStream();
			var chunk = new byte[81_920];
			var total = 0L;
			int read;
			while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
			{
				total += read;
				if (total > limit)
				{
					return StoreFetch.Fail($"'{url}' exceeded {limit} bytes.", sizeMismatch: expectedSize is > 0);
				}

				buffer.Write(chunk, 0, read);
			}

			if (expectedSize is > 0 && total != expectedSize)
			{
				return StoreFetch.Fail($"'{url}' returned {total} bytes, not the declared {expectedSize}.",
					sizeMismatch: true);
			}

			return StoreFetch.Ok(buffer.ToArray());
		}
	}
}
