using MacroDeck.Plugin.Protocol.Compatibility;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Compatibility;

/// <summary>
/// Pins the compatibility vocabularies against hard-coded literals, the way every other wire vocabulary
/// in this package is pinned. The literals are written out here on purpose: deriving them from the
/// constants under test would make the test agree with any rename, which is the one thing it exists to
/// catch. A UI keys off these exact strings, and they are append-only within a protocol major.
/// </summary>
[TestFixture]
public class CompatibilityVocabularyStabilityTests
{
	private static readonly string[] _states =
	[
		"compatible", "deprecated_apis", "update_recommended", "update_required", "partially_incompatible",
		"incompatible"
	];

	private static readonly string[] _sources = ["confirmed", "negotiated", "inferred", "unknown"];

	private static readonly string[] _severities = ["info", "warning", "error"];

	[Test]
	public void The_states_are_the_frozen_list_in_ascending_severity()
	{
		Assert.That(PluginCompatibilityStates.All, Is.EqualTo(_states));
	}

	/// <summary>
	/// The order above is not cosmetic - a plugin's state is the worst of everything found about it, so
	/// Rank has to agree with the list it is derived from. An unknown state must rank below every known
	/// one, so a state introduced by a newer host can never outrank one this host understands.
	/// </summary>
	[Test]
	public void Rank_orders_the_states_as_listed_and_puts_an_unrecognised_state_last()
	{
		Assert.Multiple(() =>
		{
			for (var index = 1; index < PluginCompatibilityStates.All.Count; index++)
			{
				var previous = PluginCompatibilityStates.All[index - 1];
				var current = PluginCompatibilityStates.All[index];

				Assert.That(PluginCompatibilityStates.Rank(current),
					Is.GreaterThan(PluginCompatibilityStates.Rank(previous)),
					$"'{current}' does not outrank '{previous}'");
			}

			Assert.That(PluginCompatibilityStates.Rank("something-a-newer-host-invented"),
				Is.LessThan(PluginCompatibilityStates.Rank("compatible")));
		});
	}

	[Test]
	public void The_finding_sources_are_the_frozen_list()
	{
		Assert.That(CompatibilityFindingSources.All, Is.EqualTo(_sources));
	}

	[Test]
	public void The_severities_are_the_frozen_list()
	{
		Assert.That(CompatibilitySeverities.All, Is.EqualTo(_severities));
	}

	[Test]
	public void An_unrecognised_value_is_never_reported_as_known()
	{
		Assert.Multiple(() =>
		{
			Assert.That(PluginCompatibilityStates.IsKnown("deprecated-apis"), Is.False);
			Assert.That(CompatibilityFindingSources.IsKnown("Confirmed"), Is.False);
			Assert.That(CompatibilitySeverities.IsKnown(null), Is.False);
		});
	}
}
