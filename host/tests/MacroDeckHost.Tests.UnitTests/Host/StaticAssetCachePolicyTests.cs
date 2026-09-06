namespace MacroDeckHost.Tests.UnitTests.Host;

public class StaticAssetCachePolicyTests
{
	[TestCase("index.html")]
	[TestCase("INDEX.HTML")]
	public void Html_documents_are_never_cached(string fileName)
	{
		Assert.That(StaticAssetCachePolicy.IsHtmlDocument(fileName), Is.True);
		Assert.That(StaticAssetCachePolicy.ResolveForFile(fileName), Is.EqualTo(StaticAssetCachePolicy.NoCache));
	}

	[TestCase("main-YN4GYHGW.js")]
	[TestCase("chunk-VKNTNHK7.js")]
	[TestCase("styles-PV2CGYJU.css")]
	[TestCase("polyfills-FVN3JHZK.js")]
	public void Fingerprinted_assets_are_cached_immutably(string fileName)
	{
		Assert.That(StaticAssetCachePolicy.IsFingerprinted(fileName), Is.True);
		Assert.That(StaticAssetCachePolicy.ResolveForFile(fileName), Is.EqualTo(StaticAssetCachePolicy.Immutable));
	}

	[TestCase("favicon.ico")]
	[TestCase("logo.webp")]
	[TestCase("robots.txt")]
	[TestCase("app.js")]
	[TestCase("styles.css")]
	public void Unhashed_files_keep_the_default_caching(string fileName)
	{
		Assert.That(StaticAssetCachePolicy.IsFingerprinted(fileName), Is.False);
		Assert.That(StaticAssetCachePolicy.ResolveForFile(fileName), Is.Null);
	}

	[TestCase("ngsw.json")]
	[TestCase("ngsw-worker.js")]
	[TestCase("safety-worker.js")]
	[TestCase("legacy-sw.js")]
	[TestCase("worker-basic.min.js")]
	[TestCase("macro-deck-worker.js")]
	[TestCase("NGSW.JSON")]
	public void Service_worker_control_files_are_never_cached(string fileName)
	{
		Assert.That(StaticAssetCachePolicy.IsServiceWorkerControlFile(fileName), Is.True);
		Assert.That(StaticAssetCachePolicy.ResolveForFile(fileName), Is.EqualTo(StaticAssetCachePolicy.NoCache));
	}

	[TestCase("manifest.webmanifest")]
	[TestCase("icon-512x512.png")]
	public void App_manifest_and_icons_keep_the_default_caching(string fileName)
	{
		Assert.That(StaticAssetCachePolicy.IsServiceWorkerControlFile(fileName), Is.False);
		Assert.That(StaticAssetCachePolicy.IsFingerprinted(fileName), Is.False);
		Assert.That(StaticAssetCachePolicy.ResolveForFile(fileName), Is.Null);
	}

	[Test]
	public void Fingerprinted_name_resembling_a_control_file_is_not_treated_as_one()
	{
		const string fileName = "ngsw-worker-YN4GYHGW.js";

		Assert.That(StaticAssetCachePolicy.IsServiceWorkerControlFile(fileName), Is.False);
		Assert.That(StaticAssetCachePolicy.IsFingerprinted(fileName), Is.True);
		Assert.That(StaticAssetCachePolicy.ResolveForFile(fileName), Is.EqualTo(StaticAssetCachePolicy.Immutable));
	}

	[Test]
	public void Html_documents_are_not_treated_as_fingerprinted()
	{
		Assert.That(StaticAssetCachePolicy.IsFingerprinted("index.html"), Is.False);
	}

	[TestCase("text/html")]
	[TestCase("text/html; charset=utf-8")]
	[TestCase("TEXT/HTML")]
	public void Html_content_types_are_recognised(string contentType)
	{
		Assert.That(StaticAssetCachePolicy.IsHtmlContentType(contentType), Is.True);
	}

	[TestCase("text/javascript")]
	[TestCase("application/json")]
	[TestCase(null)]
	public void Non_html_content_types_are_rejected(string? contentType)
	{
		Assert.That(StaticAssetCachePolicy.IsHtmlContentType(contentType), Is.False);
	}
}
