using System.Text;
using MacroDeckHost.Integrations.Http.Client;

namespace MacroDeckHost.Tests.UnitTests.Http;

[TestFixture]
internal sealed class HttpRequestFactoryTests
{
	private static readonly Dictionary<string, string> _noHeaders = new(StringComparer.Ordinal);

	[Test]
	public void Basic_auth_sends_the_correct_base64_credentials()
	{
		var result = HttpRequestFactory.CreateMessage(Spec(auth: new HttpAuthBasic("user", "secret")));

		using var message = result.Message!;
		Assert.Multiple(() =>
		{
			Assert.That(message.Headers.Authorization!.Scheme, Is.EqualTo("Basic"));
			Assert.That(message.Headers.Authorization!.Parameter,
				Is.EqualTo(Convert.ToBase64String(Encoding.UTF8.GetBytes("user:secret"))));
		});
	}

	[Test]
	public void Bearer_auth_uses_the_bearer_scheme()
	{
		var result = HttpRequestFactory.CreateMessage(Spec(auth: new HttpAuthBearer("tok-123")));

		using var message = result.Message!;
		Assert.Multiple(() =>
		{
			Assert.That(message.Headers.Authorization!.Scheme, Is.EqualTo("Bearer"));
			Assert.That(message.Headers.Authorization!.Parameter, Is.EqualTo("tok-123"));
		});
	}

	[Test]
	public void Header_auth_carries_the_value_verbatim()
	{
		var result = HttpRequestFactory.CreateMessage(Spec(auth: new HttpAuthHeader("X-Api-Key", "secret-value")));

		using var message = result.Message!;
		Assert.That(message.Headers.GetValues("X-Api-Key").Single(), Is.EqualTo("secret-value"));
	}

	[Test]
	public void Auth_lands_on_the_request_message_never_on_a_shared_default()
	{
		using var messageA = HttpRequestFactory.CreateMessage(Spec(auth: new HttpAuthBearer("token-a"))).Message!;
		using var messageB = HttpRequestFactory.CreateMessage(Spec(auth: new HttpAuthBearer("token-b"))).Message!;

		Assert.Multiple(() =>
		{
			Assert.That(messageA.Headers.Authorization!.Parameter, Is.EqualTo("token-a"));
			Assert.That(messageB.Headers.Authorization!.Parameter, Is.EqualTo("token-b"));
		});
	}

	[Test]
	public void Auth_applied_after_user_headers_wins_over_a_hand_typed_authorization_header()
	{
		var headers = new Dictionary<string, string>(StringComparer.Ordinal)
			{ ["Authorization"] = "Bearer user-typed" };

		var result = HttpRequestFactory.CreateMessage(Spec(headers: headers, auth: new HttpAuthBearer("real-token")));

		using var message = result.Message!;
		Assert.Multiple(() =>
		{
			Assert.That(message.Headers.Authorization!.Parameter, Is.EqualTo("real-token"));
			Assert.That(message.Headers.GetValues("Authorization").Count(), Is.EqualTo(1));
		});
	}

	[Test]
	public void A_user_typed_content_type_replaces_the_bodys_default_rather_than_appending()
	{
		var headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = "text/custom" };

		var result = HttpRequestFactory.CreateMessage(Spec(headers: headers, body: new HttpBodyJson("{}", null)));

		using var message = result.Message!;
		var contentTypeValues = message.Content!.Headers.GetValues("Content-Type").ToList();
		Assert.Multiple(() =>
		{
			Assert.That(contentTypeValues, Has.Count.EqualTo(1));
			Assert.That(contentTypeValues[0], Is.EqualTo("text/custom"));
		});
	}

	[Test]
	public void A_content_header_with_no_body_is_dropped_rather_than_throwing()
	{
		var headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = "text/plain" };

		HttpMessageBuildResult result = default;
		Assert.DoesNotThrow(() => result = HttpRequestFactory.CreateMessage(Spec(headers: headers)));

		using var message = result.Message!;
		Assert.That(message.Content, Is.Null);
	}

	[Test]
	public void Multipart_content_contains_the_named_file_part_and_every_form_field()
	{
		var filePath = Path.GetTempFileName();
		try
		{
			File.WriteAllBytes(filePath, [1, 2, 3]);

			var fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["caption"] = "hi", ["tag"] = "x" };
			var body = new HttpBodyMultipart(fields, filePath, "avatar");

			var result = HttpRequestFactory.CreateMessage(Spec(body: body));

			using var message = result.Message!;
			var multipart = (MultipartFormDataContent)message.Content!;
			var parts = multipart.ToList();

			var filePart = parts.Single(part => part.Headers.ContentDisposition?.Name == "avatar");
			Assert.Multiple(() =>
			{
				Assert.That(result.Failure, Is.Null);
				Assert.That(filePart.Headers.ContentDisposition!.FileName, Does.Contain(Path.GetFileName(filePath)));
				Assert.That(parts.Any(part => part.Headers.ContentDisposition?.Name == "caption"), Is.True);
				Assert.That(parts.Any(part => part.Headers.ContentDisposition?.Name == "tag"), Is.True);
			});
		}
		finally
		{
			File.Delete(filePath);
		}
	}

	[Test]
	public void To_string_on_the_spec_and_every_auth_and_body_case_never_carries_a_secret_token_or_query_string()
	{
		var spec = new HttpRequestSpec("POST",
			new Uri("https://example.invalid/path?apikey=abcd1234"),
			_noHeaders,
			new HttpAuthBasic("bob", "supersecret"),
			new HttpBodyJson("""{"secret":"jsonsecretvalue"}""", null),
			TimeSpan.FromSeconds(30),
			true,
			true,
			1_024);

		var specText = spec.ToString();

		Assert.Multiple(() =>
		{
			Assert.That(specText, Does.Not.Contain("supersecret"));
			Assert.That(specText, Does.Not.Contain("jsonsecretvalue"));
			Assert.That(specText, Does.Not.Contain("apikey"));
			Assert.That(specText, Does.Not.Contain("abcd1234"));

			Assert.That(new HttpAuthNone().ToString(), Does.Not.Contain("secret"));
			Assert.That(new HttpAuthBasic("bob", "supersecret").ToString(), Does.Not.Contain("supersecret"));
			Assert.That(new HttpAuthBearer("bearer-token-value").ToString(), Does.Not.Contain("bearer-token-value"));
			Assert.That(new HttpAuthHeader("X-Api-Key", "header-secret-value").ToString(),
				Does.Not.Contain("header-secret-value"));

			Assert.That(HttpBody.None.ToString(), Is.EqualTo("None"));
			Assert.That(new HttpBodyJson("""{"secret":"x"}""", null).ToString(), Does.Not.Contain("secret"));
			Assert.That(new HttpBodyText("plain-secret-text", null).ToString(), Does.Not.Contain("plain-secret-text"));
			Assert.That(
				new HttpBodyForm(new Dictionary<string, string> { ["password"] = "form-secret" }, null).ToString(),
				Does.Not.Contain("form-secret"));
			Assert.That(new HttpBodyMultipart(_noHeaders, "/secret/path/to/file.png", "file").ToString(),
				Does.Not.Contain("/secret/path/to/file.png"));
		});
	}

	private static HttpRequestSpec Spec(
		IReadOnlyDictionary<string, string>? headers = null,
		HttpAuth? auth = null,
		HttpBody? body = null)
		=> new("GET",
			new Uri("https://example.invalid/path"),
			headers ?? _noHeaders,
			auth ?? HttpAuth.None,
			body ?? HttpBody.None,
			TimeSpan.FromSeconds(30),
			true,
			true,
			262_144);
}
