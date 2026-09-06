using MacroDeckHost.Integrations.Http.Actions;
using MacroDeckHost.Integrations.Http.Client;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Http;

[TestFixture]
internal sealed class HttpFailureMessagesTests
{
	[TestCase(401, ActionErrorCodes.PermissionDenied)]
	[TestCase(403, ActionErrorCodes.PermissionDenied)]
	[TestCase(404, ActionErrorCodes.NotFound)]
	[TestCase(408, ActionErrorCodes.Timeout)]
	[TestCase(429, ActionErrorCodes.ProviderRejected)]
	[TestCase(400, ActionErrorCodes.ProviderRejected)]
	[TestCase(422, ActionErrorCodes.ProviderRejected)]
	[TestCase(500, ActionErrorCodes.ProviderError)]
	[TestCase(503, ActionErrorCodes.ProviderError)]
	public void The_status_table_maps_each_status_to_its_documented_code(int statusCode, string expectedCode)
	{
		var (code, _) = HttpFailureMessages.ForStatus(statusCode);
		Assert.That(code, Is.EqualTo(expectedCode));
	}

	[TestCase(100)]
	[TestCase(300)]
	public void An_unmapped_status_falls_back_to_a_generic_mismatch(int statusCode)
	{
		var (code, message) = HttpFailureMessages.ForStatus(statusCode);

		Assert.Multiple(() =>
		{
			Assert.That(code, Is.EqualTo(ActionErrorCodes.ProviderRejected));
			Assert.That(TestLocalization.Resolve(message),
				Is.EqualTo("The response status did not match what was expected."));
		});
	}

	[TestCase(HttpFailureKind.Timeout, ActionErrorCodes.Timeout)]
	[TestCase(HttpFailureKind.Unreachable, ActionErrorCodes.NotConnected)]
	[TestCase(HttpFailureKind.TlsRejected, ActionErrorCodes.NotConnected)]
	[TestCase(HttpFailureKind.FileMissing, ActionErrorCodes.NotFound)]
	[TestCase(HttpFailureKind.FileUnreadable, ActionErrorCodes.ProviderError)]
	public void The_failure_kind_table_maps_each_kind_to_its_documented_code(HttpFailureKind failure,
		string expectedCode)
	{
		var (code, _) = HttpFailureMessages.ForFailure(failure);
		Assert.That(code, Is.EqualTo(expectedCode));
	}

	[Test]
	public void No_failure_message_leaks_a_url_scheme_path_or_token_looking_value()
	{
		var messages = new List<string>();

		foreach (var failure in Enum.GetValues<HttpFailureKind>())
		{
			messages.Add(TestLocalization.Resolve(HttpFailureMessages.ForFailure(failure).Message) ?? string.Empty);
		}

		foreach (var statusCode in new[] { 100, 300, 400, 401, 403, 404, 408, 422, 429, 500, 503 })
		{
			messages.Add(TestLocalization.Resolve(HttpFailureMessages.ForStatus(statusCode).Message) ?? string.Empty);
		}

		Assert.Multiple(() =>
		{
			foreach (var message in messages)
			{
				Assert.That(message, Does.Not.Contain("http").IgnoreCase, "message contains a URL scheme");
				Assert.That(message, Does.Not.Contain('/'), "message contains a path separator");
				Assert.That(message, Does.Not.Contain("Bearer").IgnoreCase, "message contains an auth scheme");
				Assert.That(message,
					Does.Not.Match(@"[A-Za-z0-9_-]{16,}"),
					"message contains a token-looking substring");
			}
		});
	}
}
