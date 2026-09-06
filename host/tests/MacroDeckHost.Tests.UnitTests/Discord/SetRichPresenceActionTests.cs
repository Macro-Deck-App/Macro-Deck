using MacroDeckHost.Integrations.Discord.Actions;

namespace MacroDeckHost.Tests.UnitTests.Discord;

[TestFixture]
internal sealed class SetRichPresenceActionTests
{
	private static readonly DateTimeOffset _now = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

	[Test]
	public void The_activity_carries_the_configured_text()
	{
		var activity = SetRichPresenceActionDefinition.BuildActivity(
			Parameters(("details", "Editing a deck"), ("state", "Macro Deck 3"), ("showElapsed", false)),
			_now);

		Assert.Multiple(() =>
		{
			Assert.That(activity!.Details, Is.EqualTo("Editing a deck"));
			Assert.That(activity.State, Is.EqualTo("Macro Deck 3"));
			Assert.That(activity.Timestamps, Is.Null);
			Assert.That(activity.Assets, Is.Null);
		});
	}

	[Test]
	public void The_elapsed_counter_starts_now()
	{
		var activity = SetRichPresenceActionDefinition.BuildActivity(
			Parameters(("details", "Live"), ("showElapsed", true)),
			_now);

		Assert.That(activity!.Timestamps!.Start, Is.EqualTo(_now.ToUnixTimeMilliseconds()));
	}

	[Test]
	public void The_image_fields_become_activity_assets()
	{
		var activity = SetRichPresenceActionDefinition.BuildActivity(Parameters(("largeImage", "cover"),
				("largeText", "The cover"),
				("smallImage", "badge"),
				("smallText", "The badge")),
			_now);

		Assert.Multiple(() =>
		{
			Assert.That(activity!.Assets!.LargeImage, Is.EqualTo("cover"));
			Assert.That(activity.Assets.LargeText, Is.EqualTo("The cover"));
			Assert.That(activity.Assets.SmallImage, Is.EqualTo("badge"));
			Assert.That(activity.Assets.SmallText, Is.EqualTo("The badge"));
		});
	}

	[Test]
	public void An_activity_with_nothing_to_show_is_not_built()
	{
		Assert.Multiple(() =>
		{
			Assert.That(SetRichPresenceActionDefinition.BuildActivity(Parameters(), _now), Is.Null);
			Assert.That(SetRichPresenceActionDefinition.BuildActivity(Parameters(("details", "   ")), _now), Is.Null);
			Assert.That(SetRichPresenceActionDefinition.BuildActivity(Parameters(("showElapsed", true)), _now),
				Is.Null,
				"a timer alone is not an activity");
		});
	}

	[Test]
	public void An_image_tooltip_alone_is_not_enough_content()
	{
		Assert.That(SetRichPresenceActionDefinition.BuildActivity(Parameters(("largeText", "Tooltip")), _now), Is.Null);
	}

	[TestCase("0", 0)]
	[TestCase("2", 2)]
	[TestCase("3", 3)]
	[TestCase("5", 5)]
	public void The_activity_types_discord_accepts_are_passed_through(string configured, int expected)
	{
		var activity = SetRichPresenceActionDefinition.BuildActivity(
			Parameters(("details", "Live"), ("activityType", configured)),
			_now);

		Assert.That(activity!.Type, Is.EqualTo(expected));
	}

	[TestCase("1")]
	[TestCase("4")]
	[TestCase("99")]
	[TestCase("nonsense")]
	public void An_activity_type_discord_refuses_falls_back_to_playing(string configured)
	{
		var activity = SetRichPresenceActionDefinition.BuildActivity(
			Parameters(("details", "Live"), ("activityType", configured)),
			_now);

		Assert.That(activity!.Type, Is.EqualTo(0));
	}

	private static Dictionary<string, object> Parameters(params (string Name, object Value)[] values)
	{
		var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
		foreach (var (name, value) in values)
		{
			parameters[name] = value;
		}

		return parameters;
	}
}
