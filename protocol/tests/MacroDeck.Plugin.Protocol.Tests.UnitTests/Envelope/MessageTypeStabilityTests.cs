using System.Text.RegularExpressions;
using MacroDeck.Plugin.Protocol.Envelope;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Envelope;

/// <summary>
/// Pins the v1 message type set against a hard-coded literal - not derived from
/// <see cref="MessageTypes.All" /> - so renaming or deleting a type fails the build and adding one
/// forces a reviewer to confirm the append-only rule deliberately.
/// </summary>
[TestFixture]
public class MessageTypeStabilityTests
{
	private static readonly string[] _expectedTypesSortedOrdinal =
	[
		"asset.ack",
		"asset.begin",
		"asset.chunk",
		"asset.commit",
		"capability.cancel",
		"capability.declare",
		"capability.declare.ack",
		"capability.invoke",
		"capability.result",
		"event.publish",
		"flow.pause",
		"flow.resume",
		"host.asset.ack",
		"host.asset.begin",
		"host.asset.chunk",
		"host.asset.commit",
		"host.cancel",
		"host.invoke",
		"host.result",
		"host.state",
		"log.publish",
		"protocol.error",
		"session.goodbye",
		"session.hello",
		"session.ping",
		"session.pong",
		"session.welcome",
		"state.update",
	];

	private static readonly string[] _replyTypes =
	[
		MessageTypes.CapabilityResult, MessageTypes.CapabilityDeclareAck, MessageTypes.AssetAck,
		MessageTypes.HostResult, MessageTypes.HostAssetAck,
	];

	[Test]
	public void The_v1_message_type_set_is_exactly_the_frozen_literal()
	{
		var actualSorted = MessageTypes.All.OrderBy(type => type, StringComparer.Ordinal).ToArray();

		Assert.That(actualSorted, Is.EqualTo(_expectedTypesSortedOrdinal));
	}

	[Test]
	public void Every_known_message_type_has_a_direction()
	{
		Assert.Multiple(() =>
		{
			foreach (var type in MessageTypes.All)
			{
				Assert.That(MessageTypeDirections.TryGetDirection(type, out _),
					Is.True,
					$"Missing direction for '{type}'.");
			}
		});
	}

	[Test]
	public void Every_known_message_type_matches_the_domain_dot_verb_pattern()
	{
		var pattern = new Regex(@"^[a-z][a-z0-9]*(\.[a-z][a-z0-9]*)+$", RegexOptions.CultureInvariant);

		Assert.Multiple(() =>
		{
			foreach (var type in MessageTypes.All)
			{
				Assert.That(pattern.IsMatch(type), Is.True, $"'{type}' does not match the domain.verb pattern.");
			}
		});
	}

	[Test]
	public void Requires_correlation_is_true_for_exactly_the_reply_types()
	{
		Assert.Multiple(() =>
		{
			foreach (var type in MessageTypes.All)
			{
				var expected = _replyTypes.Contains(type, StringComparer.Ordinal);
				Assert.That(MessageTypes.RequiresCorrelation(type),
					Is.EqualTo(expected),
					$"Unexpected RequiresCorrelation for '{type}'.");
			}
		});
	}

	[Test]
	public void Protocol_error_does_not_require_correlation()
		=> Assert.That(MessageTypes.RequiresCorrelation(MessageTypes.ProtocolError), Is.False);

	[TestCaseSource(nameof(_replyTypes))]
	public void Every_reply_type_requires_correlation(string type)
		=> Assert.That(MessageTypes.RequiresCorrelation(type), Is.True);

	[Test]
	public void No_duplicate_message_types()
		=> Assert.That(MessageTypes.All, Is.Unique);
}
