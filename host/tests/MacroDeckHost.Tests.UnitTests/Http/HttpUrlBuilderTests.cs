using MacroDeckHost.Integrations.Http.Client;

namespace MacroDeckHost.Tests.UnitTests.Http;

[TestFixture]
internal sealed class HttpUrlBuilderTests
{
	[TestCase("http://example.invalid/path")]
	[TestCase("https://example.invalid/path")]
	public void An_absolute_http_or_https_url_is_accepted(string url)
	{
		Assert.That(HttpUrlBuilder.TryBuild(url, null, out _), Is.True);
	}

	[TestCase("http://127.0.0.1:8080/x")]
	[TestCase("http://192.168.1.5/")]
	[TestCase("http://localhost/")]
	[TestCase("http://10.0.0.1/")]
	public void A_loopback_or_private_network_target_is_accepted_not_blocked(string url)
	{
		Assert.That(HttpUrlBuilder.TryBuild(url, null, out var uri), Is.True);
		Assert.That(uri, Is.Not.Null);
	}

	[TestCase("ftp://example.invalid/file")]
	[TestCase("file:///etc/passwd")]
	[TestCase("example.invalid/path")]
	[TestCase("")]
	[TestCase("   ")]
	[TestCase("not a url")]
	[TestCase(null)]
	public void A_non_http_url_is_rejected(string? url)
	{
		Assert.That(HttpUrlBuilder.TryBuild(url, null, out var uri), Is.False);
		Assert.That(uri, Is.Null);
	}

	[Test]
	public void Query_parameters_percent_encode_both_key_and_value()
	{
		var query = new Dictionary<string, string>(StringComparer.Ordinal) { ["na me"] = "a=b&c" };

		Assert.That(HttpUrlBuilder.TryBuild("https://example.invalid/path", query, out var uri), Is.True);

		Assert.That(uri!.Query, Is.EqualTo("?na%20me=a%3Db%26c"));
	}

	[Test]
	public void Query_parameters_merge_onto_a_url_that_already_carries_a_query()
	{
		var query = new Dictionary<string, string>(StringComparer.Ordinal) { ["b"] = "2" };

		Assert.That(HttpUrlBuilder.TryBuild("https://example.invalid/path?a=1", query, out var uri), Is.True);

		Assert.That(uri!.Query, Is.EqualTo("?a=1&b=2"));
	}

	[Test]
	public void A_blank_key_is_dropped_from_the_query()
	{
		var query = new Dictionary<string, string>(StringComparer.Ordinal) { [""] = "ignored", ["kept"] = "1" };

		Assert.That(HttpUrlBuilder.TryBuild("https://example.invalid/path", query, out var uri), Is.True);

		Assert.That(uri!.Query, Is.EqualTo("?kept=1"));
	}

	[Test]
	public void An_empty_query_dictionary_leaves_the_url_untouched()
	{
		Assert.That(HttpUrlBuilder.TryBuild("https://example.invalid/path?a=1",
				new Dictionary<string, string>(),
				out var uri),
			Is.True);

		Assert.That(uri!.ToString(), Is.EqualTo("https://example.invalid/path?a=1"));
	}
}
