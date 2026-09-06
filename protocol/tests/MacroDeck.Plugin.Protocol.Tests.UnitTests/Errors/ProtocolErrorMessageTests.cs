using System.Globalization;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Errors;

[TestFixture]
public class ProtocolErrorMessageTests
{
	[TestCaseSource(typeof(ProtocolErrorCodes), nameof(ProtocolErrorCodes.All))]
	public void Every_code_has_a_non_empty_message_within_the_max_error_message_length(string code)
	{
		var message = ProtocolErrorMessages.For(code);

		Assert.Multiple(() =>
		{
			Assert.That(message, Is.Not.Null.And.Not.Empty);
			Assert.That(message.Length, Is.LessThanOrEqualTo(ProtocolLimits.MaxErrorMessageLength));
		});
	}

	[TestCaseSource(typeof(ProtocolErrorCodes), nameof(ProtocolErrorCodes.All))]
	public void No_message_carries_an_interpolation_slot_or_a_credential_bearing_word(string code)
	{
		var message = ProtocolErrorMessages.For(code);
		var lowerMessage = message.ToLower(CultureInfo.InvariantCulture);

		Assert.Multiple(() =>
		{
			Assert.That(message, Does.Not.Contain('{'));
			Assert.That(lowerMessage, Does.Not.Contain("secret"));
			Assert.That(lowerMessage, Does.Not.Contain("token"));
			Assert.That(lowerMessage, Does.Not.Contain("password"));
			Assert.That(lowerMessage, Does.Not.Contain("credential"));
		});
	}

	[Test]
	public void An_unrecognised_code_returns_the_safe_fallback_rather_than_throwing_or_echoing_the_code()
	{
		const string unknownCode = "NOT_A_REAL_CODE";

		string message = null!;
		Assert.DoesNotThrow(() => message = ProtocolErrorMessages.For(unknownCode));

		Assert.Multiple(() =>
		{
			Assert.That(message, Is.Not.Null.And.Not.Empty);
			Assert.That(message, Does.Not.Contain(unknownCode));
		});
	}

	[Test]
	public void The_message_for_a_known_code_is_stable_and_deterministic()
	{
		var first = ProtocolErrorMessages.For(ProtocolErrorCodes.Timeout);
		var second = ProtocolErrorMessages.For(ProtocolErrorCodes.Timeout);

		Assert.That(first, Is.EqualTo(second));
	}
}
