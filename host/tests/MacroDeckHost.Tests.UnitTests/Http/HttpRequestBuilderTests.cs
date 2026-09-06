using MacroDeckHost.Integrations.Http.Actions;
using MacroDeckHost.Integrations.Http.Client;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Http;

[TestFixture]
internal sealed class HttpRequestBuilderTests
{
	[Test]
	public void A_blank_method_defaults_to_get()
	{
		Assert.That(HttpRequestBuilder.TryBuild(Parameters(("url", "https://example.invalid/")), out var spec, out _),
			Is.True);

		Assert.That(spec.Method, Is.EqualTo("GET"));
	}

	[Test]
	public void With_no_body_type_configured_the_body_is_none()
	{
		Assert.That(HttpRequestBuilder.TryBuild(Parameters(("url", "https://example.invalid/")), out var spec, out _),
			Is.True);

		Assert.That(spec.Body, Is.InstanceOf<HttpBodyNone>());
	}

	[Test]
	public void Redirects_and_tls_validation_default_to_on()
	{
		Assert.That(HttpRequestBuilder.TryBuild(Parameters(("url", "https://example.invalid/")), out var spec, out _),
			Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(spec.FollowRedirects, Is.True);
			Assert.That(spec.ValidateTls, Is.True);
		});
	}

	[Test]
	public void The_timeout_and_max_response_size_default_to_their_stated_values()
	{
		Assert.That(HttpRequestBuilder.TryBuild(Parameters(("url", "https://example.invalid/")), out var spec, out _),
			Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(spec.Timeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
			Assert.That(spec.MaxResponseBytes, Is.EqualTo(262_144));
		});
	}

	[Test]
	public void A_blank_url_is_rejected_with_a_specific_message()
	{
		Assert.That(HttpRequestBuilder.TryBuild(Parameters(), out _, out var error), Is.False);
		Assert.That(TestLocalization.Resolve(error), Is.EqualTo("No URL was configured."));
	}

	[Test]
	public void A_non_http_url_is_rejected_with_a_specific_message()
	{
		Assert.That(HttpRequestBuilder.TryBuild(Parameters(("url", "ftp://example.invalid/")), out _, out var error),
			Is.False);
		Assert.That(TestLocalization.Resolve(error),
			Is.EqualTo("The URL must be an absolute http:// or https:// address."));
	}

	[Test]
	public void An_invalid_method_token_is_rejected_with_a_specific_message()
	{
		Assert.That(HttpRequestBuilder.TryBuild(
				Parameters(("url", "https://example.invalid/"), ("method", "PUT DELETE")),
				out _,
				out var error),
			Is.False);
		Assert.That(TestLocalization.Resolve(error), Is.EqualTo("The HTTP method is not a valid token."));
	}

	[Test]
	public void Basic_auth_with_no_username_and_no_secret_is_rejected()
	{
		Assert.That(HttpRequestBuilder.TryBuild(Parameters(("url", "https://example.invalid/"), ("authType", "basic")),
				out _,
				out var error),
			Is.False);
		Assert.That(TestLocalization.Resolve(error),
			Is.EqualTo("Basic authentication needs a username, a password, or both."));
	}

	[Test]
	public void Bearer_auth_with_no_token_is_rejected()
	{
		Assert.That(HttpRequestBuilder.TryBuild(Parameters(("url", "https://example.invalid/"), ("authType", "bearer")),
				out _,
				out var error),
			Is.False);
		Assert.That(TestLocalization.Resolve(error), Is.EqualTo("Bearer authentication needs a token."));
	}

	[Test]
	public void Header_auth_with_no_header_name_is_rejected()
	{
		Assert.That(HttpRequestBuilder.TryBuild(Parameters(("url", "https://example.invalid/"), ("authType", "header")),
				out _,
				out var error),
			Is.False);
		Assert.That(TestLocalization.Resolve(error), Is.EqualTo("Header authentication needs a header name."));
	}

	[Test]
	public void An_invalid_json_body_is_rejected()
	{
		Assert.That(HttpRequestBuilder.TryBuild(
				Parameters(("url", "https://example.invalid/"), ("bodyType", "json"), ("jsonBody", "{not json")),
				out _,
				out var error),
			Is.False);
		Assert.That(TestLocalization.Resolve(error), Is.EqualTo("The JSON body is not valid JSON."));
	}

	[Test]
	public void An_invalid_content_type_is_rejected()
	{
		Assert.That(HttpRequestBuilder.TryBuild(
				Parameters(("url", "https://example.invalid/"), ("contentType", "definitely not a content type")),
				out _,
				out var error),
			Is.False);
		Assert.That(TestLocalization.Resolve(error), Is.EqualTo("The content type is not valid."));
	}

	[Test]
	public void Multipart_with_no_file_is_rejected()
	{
		Assert.That(HttpRequestBuilder.TryBuild(
				Parameters(("url", "https://example.invalid/"), ("bodyType", "multipart")),
				out _,
				out var error),
			Is.False);
		Assert.That(TestLocalization.Resolve(error), Is.EqualTo("A multipart request needs a file."));
	}

	[Test]
	public void A_content_type_override_replaces_the_bodys_default_content_type()
	{
		Assert.That(HttpRequestBuilder.TryBuild(Parameters(("url", "https://example.invalid/"),
					("bodyType", "json"),
					("jsonBody", "{}"),
					("contentType", "application/vnd.custom+json")),
				out var spec,
				out _),
			Is.True);

		var json = (HttpBodyJson)spec.Body;
		Assert.That(json.ContentType, Is.EqualTo("application/vnd.custom+json"));
	}

	[TestCase(500d, 1_000d)]
	[TestCase(999_999d, 300_000d)]
	public void The_timeout_is_clamped_at_both_bounds(double requestedMs, double expectedMs)
	{
		Assert.That(HttpRequestBuilder.TryBuild(
				Parameters(("url", "https://example.invalid/"), ("timeout", requestedMs)),
				out var spec,
				out _),
			Is.True);

		Assert.That(spec.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(expectedMs)));
	}

	[TestCase(10d, 1_024L)]
	[TestCase(99_999_999d, 10_485_760L)]
	public void The_max_response_size_is_clamped_at_both_bounds(double requested, long expected)
	{
		Assert.That(HttpRequestBuilder.TryBuild(
				Parameters(("url", "https://example.invalid/"), ("maxResponseBytes", requested)),
				out var spec,
				out _),
			Is.True);

		Assert.That(spec.MaxResponseBytes, Is.EqualTo(expected));
	}

	[Test]
	public void A_multipart_body_forces_follow_redirects_off_even_when_requested_on()
	{
		Assert.That(HttpRequestBuilder.TryBuild(Parameters(("url", "https://example.invalid/"),
					("bodyType", "multipart"),
					("filePath", "/tmp/some-file.png"),
					("followRedirects", true)),
				out var spec,
				out _),
			Is.True);

		Assert.That(spec.FollowRedirects, Is.False);
	}

	[TestCase(double.NaN)]
	[TestCase(double.PositiveInfinity)]
	[TestCase(double.NegativeInfinity)]
	public void A_non_finite_timeout_falls_back_to_the_default(double requested)
	{
		Assert.That(HttpRequestBuilder.TryBuild(Parameters(("url", "https://example.invalid/"), ("timeout", requested)),
				out var spec,
				out _),
			Is.True);

		Assert.That(spec.Timeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
	}

	[Test]
	public void A_non_finite_max_response_size_falls_back_to_the_default()
	{
		Assert.That(HttpRequestBuilder.TryBuild(
				Parameters(("url", "https://example.invalid/"), ("maxResponseBytes", double.NaN)),
				out var spec,
				out _),
			Is.True);

		Assert.That(spec.MaxResponseBytes, Is.EqualTo(262_144L));
	}

	[TestCase("get", "GET")]
	[TestCase("Patch", "PATCH")]
	[TestCase("  delete  ", "DELETE")]
	public void A_hand_typed_method_is_upper_cased(string typed, string expected)
	{
		Assert.That(HttpRequestBuilder.TryBuild(Parameters(("url", "https://example.invalid/"), ("method", typed)),
				out var spec,
				out _),
			Is.True);

		Assert.That(spec.Method, Is.EqualTo(expected));
	}

	private static Dictionary<string, object> Parameters(params (string Name, object Value)[] values)
		=> values.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
}
