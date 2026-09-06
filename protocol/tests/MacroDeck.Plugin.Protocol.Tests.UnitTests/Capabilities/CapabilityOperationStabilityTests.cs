using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Capabilities;

/// <summary>
/// Pins the per-kind operation vocabulary against hard-coded literals, the same technique
/// <see cref="Envelope.MessageTypeStabilityTests" /> uses for message types - so renaming or
/// removing an operation fails the build and adding one forces a reviewer to confirm it
/// deliberately.
/// </summary>
[TestFixture]
public class CapabilityOperationStabilityTests
{
	private static readonly IReadOnlyDictionary<string, string[]> _expected
		= new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			[CapabilityKinds.Actions] = ["describe", "execute", "options", "state", "icon", "icon.content"],
			[CapabilityKinds.Variables] = ["describe", "get", "set", "discover", "resolve", "subscribe"],
			[CapabilityKinds.Events] = ["describe", "options"],
			[CapabilityKinds.Icons] = ["describe"],
			[CapabilityKinds.Migration] = ["describe", "migrate-action", "migrate-configuration"],
			[CapabilityKinds.ConfigFlow] = ["describe", "flow.start", "flow.submit", "flow.abandon"],
			[CapabilityKinds.MusicPlayer] =
			[
				"describe", "instances", "state", "artwork", "play", "play-item", "pause", "toggle", "next",
				"previous", "seek", "volume", "shuffle", "repeat", "catalog", "devices", "transfer",
			],
			[CapabilityKinds.Weather] = ["describe", "instances", "snapshot"],
			[CapabilityKinds.VirtualProfiles] = ["describe", "profiles", "widget-interaction"],
			[CapabilityKinds.Issues] = ["describe", "list", "resolve"],
			[CapabilityKinds.Ui] =
			[
				"describe", "session.open", "session.close", "session.snapshot", "session.event",
				"modal.result",
			],
			[CapabilityKinds.Localization] = ["describe", "catalog"],
			[CapabilityKinds.DeviceProvider] =
				["describe", "devices", "session.open", "session.surface", "session.close"],
			[CapabilityKinds.LayoutProvider] = ["describe", "layouts"],
			[CapabilityKinds.FolderViewProvider] = ["describe", "folder-views"],
			[CapabilityKinds.WidgetTypeProvider] = ["describe", "widget-types"],
		};

	[Test]
	public void Every_capability_kind_has_a_frozen_operation_list()
	{
		Assert.Multiple(() =>
		{
			foreach (var (kind, operations) in _expected)
			{
				Assert.That(CapabilityOperations.For(kind), Is.EqualTo(operations), $"operations for '{kind}'");
			}
		});
	}

	[Test]
	public void For_covers_exactly_the_known_capability_kinds()
	{
		Assert.That(_expected.Keys, Is.EquivalentTo(CapabilityKinds.All));
	}

	[TestCaseSource(nameof(_kindsAndOperations))]
	public void Is_known_is_true_for_every_documented_pair(string kind, string operation)
		=> Assert.That(CapabilityOperations.IsKnown(kind, operation), Is.True);

	[Test]
	public void Is_known_is_false_for_an_unknown_operation()
		=> Assert.That(CapabilityOperations.IsKnown(CapabilityKinds.Actions, "not-a-real-operation"), Is.False);

	[Test]
	public void Is_known_is_false_for_an_unknown_kind()
		=> Assert.That(CapabilityOperations.IsKnown("not-a-real-kind", "describe"), Is.False);

	[Test]
	public void No_duplicate_operations_within_a_kind()
	{
		Assert.Multiple(() =>
		{
			foreach (var (kind, operations) in _expected)
			{
				Assert.That(operations, Is.Unique, $"duplicate operation for '{kind}'");
			}
		});
	}

	private static IEnumerable<TestCaseData> _kindsAndOperations()
		=> _expected.SelectMany(pair => pair.Value.Select(operation => new TestCaseData(pair.Key, operation)));
}
