using System.Text.RegularExpressions;

namespace MacroDeckHost;

public static partial class StaticAssetCachePolicy
{
	public const string NoCache = "no-cache, no-store, must-revalidate";

	public const string Immutable = "public, max-age=31536000, immutable";

	private const string HtmlContentType = "text/html";

	// Includes the names the retired Angular client used. Nothing emits them any more, but a browser
	// that installed one before the upgrade still has it, and it is only replaced if the response
	// carrying it is never cached.

	private static readonly HashSet<string> ServiceWorkerControlFiles = new(StringComparer.OrdinalIgnoreCase)
	{
		"ngsw.json",
		"ngsw-worker.js",
		"safety-worker.js",
		"worker-basic.min.js",
		"legacy-sw.js",
		"macro-deck-worker.js",
	};

	[GeneratedRegex(@"-[A-Za-z0-9]{8,}\.(?:js|css)$", RegexOptions.CultureInvariant)]
	private static partial Regex FingerprintedAssetRegex();

	public static bool IsFingerprinted(string fileName)
	{
		return FingerprintedAssetRegex().IsMatch(fileName);
	}

	public static bool IsServiceWorkerControlFile(string fileName)
	{
		return ServiceWorkerControlFiles.Contains(fileName);
	}

	public static bool IsHtmlDocument(string fileName)
	{
		return fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase);
	}

	public static string? ResolveForFile(string fileName)
	{
		if (IsHtmlDocument(fileName) || IsServiceWorkerControlFile(fileName))
		{
			return NoCache;
		}

		return IsFingerprinted(fileName) ? Immutable : null;
	}

	public static bool IsHtmlContentType(string? contentType)
	{
		return contentType is not null && contentType.Contains(HtmlContentType, StringComparison.OrdinalIgnoreCase);
	}
}
