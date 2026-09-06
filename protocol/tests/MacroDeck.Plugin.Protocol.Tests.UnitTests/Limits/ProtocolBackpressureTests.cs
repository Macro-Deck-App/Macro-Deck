using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Limits;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Limits;

[TestFixture]
public class ProtocolBackpressureTests
{
	private static readonly string[] _expectedExemptTypes =
	[
		MessageTypes.CapabilityResult,
		MessageTypes.CapabilityDeclareAck,
		MessageTypes.AssetAck,
		MessageTypes.HostAssetAck,
		MessageTypes.SessionPing,
		MessageTypes.SessionPong,
		MessageTypes.SessionGoodbye,
		MessageTypes.FlowPause,
		MessageTypes.FlowResume,
		MessageTypes.CapabilityCancel,
		MessageTypes.ProtocolError,
		MessageTypes.HostInvoke,
		MessageTypes.HostResult,
		MessageTypes.HostCancel,
	];

	private static readonly string[] _replyTypes =
	[
		MessageTypes.CapabilityResult, MessageTypes.CapabilityDeclareAck, MessageTypes.AssetAck,
		MessageTypes.HostAssetAck,
	];

	[Test]
	public void The_exempt_set_is_exactly_the_documented_fourteen_types()
		=> Assert.That(_expectedExemptTypes, Has.Length.EqualTo(14));

	[TestCaseSource(nameof(_expectedExemptTypes))]
	public void Every_documented_type_is_exempt_while_paused(string type)
		=> Assert.That(ProtocolBackpressure.IsExemptWhilePaused(type), Is.True);

	[TestCaseSource(nameof(_replyTypes))]
	public void Every_reply_type_is_exempt_while_paused_because_replies_drain_rather_than_grow_the_queue(string type)
		=> Assert.That(ProtocolBackpressure.IsExemptWhilePaused(type), Is.True);

	[Test]
	public void Every_non_exempt_known_type_is_blocked_while_paused()
	{
		var nonExempt = MessageTypes.All.Except(_expectedExemptTypes, StringComparer.Ordinal);

		Assert.Multiple(() =>
		{
			foreach (var type in nonExempt)
			{
				Assert.That(ProtocolBackpressure.IsExemptWhilePaused(type),
					Is.False,
					$"'{type}' should be blocked while paused.");
			}
		});
	}

	[Test]
	public void An_unknown_type_is_not_exempt()
		=> Assert.That(ProtocolBackpressure.IsExemptWhilePaused("not.a.real.type"), Is.False);
}
