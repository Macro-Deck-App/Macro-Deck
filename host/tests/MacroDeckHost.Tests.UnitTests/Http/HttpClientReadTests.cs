using System.Text;
using MacroDeckHost.Integrations.Http.Client;

namespace MacroDeckHost.Tests.UnitTests.Http;

[TestFixture]
internal sealed class HttpClientReadTests
{
	[Test]
	public async Task A_body_under_the_limit_reads_whole_and_is_not_marked_truncated()
	{
		var body = Encoding.UTF8.GetBytes("hello world");
		var client = ClientReturning(body);

		var outcome = await client.SendAsync(Spec(maxResponseBytes: 1_000), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Response!.Body, Is.EqualTo("hello world"));
			Assert.That(outcome.Response!.BodyTruncated, Is.False);
		});
	}

	[Test]
	public async Task A_body_over_the_limit_truncates_exactly_at_the_boundary()
	{
		var body = Encoding.UTF8.GetBytes("0123456789ABCDEFGHIJKLMNO");
		var client = ClientReturning(body);

		var outcome = await client.SendAsync(Spec(maxResponseBytes: 10), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Response!.Body, Is.EqualTo("0123456789"));
			Assert.That(outcome.Response!.BodyTruncated, Is.True);
		});
	}

	[Test]
	public async Task A_body_exactly_at_the_limit_is_not_marked_truncated()
	{
		var body = Encoding.UTF8.GetBytes("0123456789");
		var client = ClientReturning(body);

		var outcome = await client.SendAsync(Spec(maxResponseBytes: 10), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Response!.Body, Is.EqualTo("0123456789"));
			Assert.That(outcome.Response!.BodyTruncated, Is.False);
		});
	}

	[Test]
	public async Task A_quoted_charset_decodes_with_the_requested_encoding()
	{
		var latin1 = Encoding.GetEncoding("iso-8859-1");
		var body = latin1.GetBytes("café");
		var client = ClientReturning(body, contentType: "text/plain; charset=\"iso-8859-1\"");

		var outcome = await client.SendAsync(Spec(), CancellationToken.None);

		Assert.That(outcome.Response!.Body, Is.EqualTo("café"));
	}

	[Test]
	public async Task An_unsupported_charset_falls_back_to_utf8()
	{
		var body = Encoding.UTF8.GetBytes("hello");
		var client = ClientReturning(body, contentType: "text/plain; charset=windows-1252");

		var outcome = await client.SendAsync(Spec(), CancellationToken.None);

		Assert.That(outcome.Response!.Body, Is.EqualTo("hello"));
	}

	[Test]
	public async Task Response_content_headers_appear_in_the_merged_header_map()
	{
		var client = ClientReturning([],
			contentType: "application/json",
			headers: [("X-Custom", "abc")]);

		var outcome = await client.SendAsync(Spec(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Response!.Headers["Content-Type"], Is.EqualTo("application/json"));
			Assert.That(outcome.Response!.Headers["X-Custom"], Is.EqualTo("abc"));
		});
	}

	[Test]
	public async Task Repeated_header_names_are_joined_with_a_comma_and_space()
	{
		var client = ClientReturning([], headers: [("Set-Cookie", "a=1"), ("Set-Cookie", "b=2")]);

		var outcome = await client.SendAsync(Spec(), CancellationToken.None);

		Assert.That(outcome.Response!.Headers["Set-Cookie"], Is.EqualTo("a=1, b=2"));
	}

	[Test]
	public async Task Header_lookup_is_case_insensitive()
	{
		var client = ClientReturning([], headers: [("X-Custom", "abc")]);

		var outcome = await client.SendAsync(Spec(), CancellationToken.None);

		Assert.That(outcome.Response!.Headers["x-custom"], Is.EqualTo("abc"));
	}

	[Test]
	public async Task A_handler_that_never_answers_yields_a_timeout_after_the_spec_deadline()
	{
		var client = new HttpRequestClient((_, _) =>
			new HttpClient(FakeHttpMessageHandler.NeverAnswers()) { Timeout = Timeout.InfiniteTimeSpan });

		var outcome = await client.SendAsync(Spec(timeout: TimeSpan.FromMilliseconds(50)), CancellationToken.None);

		Assert.That(outcome.Failure, Is.EqualTo(HttpFailureKind.Timeout));
	}

	[Test]
	public void A_pre_cancelled_caller_token_propagates_rather_than_being_reported_as_a_timeout()
	{
		var client = new HttpRequestClient((_, _) =>
			new HttpClient(FakeHttpMessageHandler.NeverAnswers()) { Timeout = Timeout.InfiniteTimeSpan });

		using var cts = new CancellationTokenSource();
		cts.Cancel();

		Assert.That(async () => await client.SendAsync(Spec(), cts.Token),
			Throws.InstanceOf<OperationCanceledException>());
	}

	private static HttpRequestClient ClientReturning(
		byte[] body,
		string? contentType = null,
		IEnumerable<(string Name, string Value)>? headers = null)
		=> new((_, _) => new HttpClient(new FakeHttpMessageHandler(body, contentType: contentType, headers: headers))
		{
			Timeout = Timeout.InfiniteTimeSpan
		});

	private static HttpRequestSpec Spec(TimeSpan? timeout = null, long maxResponseBytes = 262_144)
		=> new("GET",
			new Uri("https://example.invalid/"),
			new Dictionary<string, string>(StringComparer.Ordinal),
			HttpAuth.None,
			HttpBody.None,
			timeout ?? TimeSpan.FromSeconds(30),
			true,
			true,
			maxResponseBytes);
}
