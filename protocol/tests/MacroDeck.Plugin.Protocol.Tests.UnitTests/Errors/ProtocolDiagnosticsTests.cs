using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Errors;

[TestFixture]
public class ProtocolDiagnosticsTests
{
	[TestCase("secret")]
	[TestCase("Secret")]
	[TestCase("SECRET")]
	[TestCase("api-key")]
	[TestCase("api_key")]
	[TestCase("apikey")]
	[TestCase("token")]
	[TestCase("accessToken")]
	[TestCase("password")]
	[TestCase("Authorization")]
	[TestCase("credential")]
	[TestCase("someCredentialValue")]
	public void Sensitive_keys_are_masked_case_and_separator_insensitively(string key)
	{
		var details = new Dictionary<string, string>(StringComparer.Ordinal) { [key] = "raw-value" };

		var redacted = ProtocolDiagnostics.Redact(details);

		Assert.That(redacted[key], Is.Not.EqualTo("raw-value"));
	}

	[Test]
	public void Every_masked_value_uses_the_same_redaction_marker()
	{
		var details = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["secret"] = "s1",
			["password"] = "p1",
			["apiKey"] = "k1",
		};

		var redacted = ProtocolDiagnostics.Redact(details);

		Assert.Multiple(() =>
		{
			Assert.That(redacted["secret"], Is.EqualTo(redacted["password"]));
			Assert.That(redacted["password"], Is.EqualTo(redacted["apiKey"]));
		});
	}

	[Test]
	public void Benign_keys_are_left_intact()
	{
		var details = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["deviceId"] = "abc-123",
			["reason"] = "timeout",
		};

		var redacted = ProtocolDiagnostics.Redact(details);

		Assert.Multiple(() =>
		{
			Assert.That(redacted["deviceId"], Is.EqualTo("abc-123"));
			Assert.That(redacted["reason"], Is.EqualTo("timeout"));
		});
	}

	[Test]
	public void Redact_truncates_to_max_error_detail_entries()
	{
		var details = new Dictionary<string, string>(StringComparer.Ordinal);
		for (var i = 0; i < ProtocolLimits.MaxErrorDetailEntries + 25; i++)
		{
			details[$"key-{i}"] = $"value-{i}";
		}

		var redacted = ProtocolDiagnostics.Redact(details);

		Assert.That(redacted, Has.Count.EqualTo(ProtocolLimits.MaxErrorDetailEntries));
	}

	[Test]
	public void Redact_of_null_returns_an_empty_dictionary_rather_than_throwing()
	{
		IReadOnlyDictionary<string, string> redacted = null!;
		Assert.DoesNotThrow(() => redacted = ProtocolDiagnostics.Redact(null));

		Assert.That(redacted, Is.Empty);
	}

	[Test]
	public void Redact_of_an_empty_dictionary_returns_an_empty_dictionary()
		=> Assert.That(ProtocolDiagnostics.Redact(new Dictionary<string, string>(StringComparer.Ordinal)), Is.Empty);
}
