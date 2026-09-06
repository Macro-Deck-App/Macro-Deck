using MacroDeck.Plugin.Protocol.Handshake;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Handshake;

[TestFixture]
public class PluginSessionIdTests
{
	[TestCase("0f8fad5b-d9cb-7f5b-9165-70867728950e")]
	[TestCase("00000000-0000-7000-8000-000000000000")]
	[TestCase("ffffffff-ffff-7fff-bfff-ffffffffffff")]
	public void Canonical_lowercase_dashed_uuid_v7_is_valid(string sessionId)
		=> Assert.That(PluginSessionId.IsValid(sessionId), Is.True);

	[Test]
	public void Null_is_invalid()
		=> Assert.That(PluginSessionId.IsValid(null), Is.False);

	[TestCase("")]
	[TestCase("0F8FAD5B-D9CB-7F5B-9165-70867728950E")]
	[TestCase("0f8fad5b-d9cb-4f5b-9165-70867728950e")]
	[TestCase("0f8fad5b-d9cb-7f5b-1165-70867728950e")]
	[TestCase("0f8fad5b-d9cb-7f5b-9165-70867728950")]
	[TestCase("0f8fad5b-d9cb-7f5b-9165-70867728950ee")]
	[TestCase("0f8fad5bd9cb7f5b916570867728950e")]
	[TestCase("0f8fad5b_d9cb_7f5b_9165_70867728950e")]
	[TestCase("not-a-uuid-at-all")]
	[TestCase("0f8fad5b-d9cb-7f5b-9165-70867728950e\n")]
	[TestCase("0f8fad5b-d9cb-7f5b-9165-70867728950e ")]
	[TestCase(" 0f8fad5b-d9cb-7f5b-9165-70867728950e")]
	public void Malformed_ids_are_invalid(string sessionId)
		=> Assert.That(PluginSessionId.IsValid(sessionId), Is.False);

	[TestCase('8')]
	[TestCase('9')]
	[TestCase('a')]
	[TestCase('b')]
	public void Every_documented_variant_nibble_is_valid(char variantNibble)
		=> Assert.That(PluginSessionId.IsValid($"0f8fad5b-d9cb-7f5b-{variantNibble}165-70867728950e"), Is.True);

	[TestCase('0')]
	[TestCase('7')]
	[TestCase('c')]
	[TestCase('f')]
	public void A_variant_nibble_outside_89ab_is_invalid(char variantNibble)
		=> Assert.That(PluginSessionId.IsValid($"0f8fad5b-d9cb-7f5b-{variantNibble}165-70867728950e"), Is.False);
}
