using System.Text.RegularExpressions;
using MacroDeck.Plugin.Protocol.Errors;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Errors;

/// <summary>
/// Pins the v1 error code set against a hard-coded literal - not derived from
/// <see cref="ProtocolErrorCodes.All" /> - so renaming or deleting a code fails the build, and adding
/// one fails until the literal is edited deliberately, forcing the reviewer to confirm the
/// append-only rule.
/// </summary>
[TestFixture]
public class ProtocolErrorCodeStabilityTests
{
	private static readonly string[] _expectedCodesSortedOrdinal =
	[
		"ASSET_TOO_LARGE",
		"CANCELLED",
		"CAPABILITY_UNAVAILABLE",
		"CAPABILITY_UNSUPPORTED",
		"CORRELATION_UNKNOWN",
		"DUPLICATE_IDEMPOTENCY_KEY",
		"INTERNAL_ERROR",
		"INVALID_PAYLOAD",
		"MALFORMED_ENVELOPE",
		"PAYLOAD_TOO_LARGE",
		"PLUGIN_ALREADY_REGISTERED",
		"PROTOCOL_VERSION_UNSUPPORTED",
		"QUEUE_OVERFLOW",
		"RATE_LIMITED",
		"SESSION_EXPIRED",
		"SESSION_NOT_FOUND",
		"SESSION_NOT_RESUMABLE",
		"SESSION_REPLACED",
		"TIMEOUT",
		"UNAUTHENTICATED",
		"UNKNOWN_MESSAGE_TYPE",
	];

	[Test]
	public void The_v1_error_code_set_is_exactly_the_frozen_literal()
	{
		var actualSorted = ProtocolErrorCodes.All.OrderBy(code => code, StringComparer.Ordinal).ToArray();

		Assert.That(actualSorted, Is.EqualTo(_expectedCodesSortedOrdinal));
	}

	[Test]
	public void Every_code_matches_the_screaming_snake_case_pattern()
	{
		var pattern = new Regex("^[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant);

		Assert.Multiple(() =>
		{
			foreach (var code in ProtocolErrorCodes.All)
			{
				Assert.That(pattern.IsMatch(code), Is.True, $"'{code}' does not match ^[A-Z][A-Z0-9_]*$.");
			}
		});
	}

	[Test]
	public void There_are_no_duplicate_codes()
		=> Assert.That(ProtocolErrorCodes.All, Is.Unique);
}
