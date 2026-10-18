namespace MacroDeck.Sdk.Ui;

internal static class UiEventOriginUrls
{
	public static void EnsureWebUrl(Uri url)
	{
		ArgumentNullException.ThrowIfNull(url);

		if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
		{
			throw new ArgumentException("Only absolute http and https URLs can be opened.", nameof(url));
		}
	}
}
