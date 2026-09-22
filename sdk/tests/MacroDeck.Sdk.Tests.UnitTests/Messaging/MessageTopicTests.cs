using MacroDeck.Sdk.Messaging;

namespace MacroDeck.Sdk.Tests.UnitTests.Messaging;

[TestFixture]
public class MessageTopicTests
{
	[TestCase("obs.scene.changed")]
	[TestCase("home-assistant.light_1.on")]
	[TestCase("a.b")]
	[TestCase("v2.status")]
	public void A_well_formed_topic_is_valid(string topic)
		=> Assert.That(MessageTopic.IsValidTopic(topic), Is.True);

	[TestCase("")]
	[TestCase("obs")]
	[TestCase("Obs.scene")]
	[TestCase("obs..scene")]
	[TestCase("obs.scene.")]
	[TestCase("obs.-scene")]
	[TestCase("obs.scene-")]
	[TestCase("obs.sce ne")]
	[TestCase("obs.*")]
	[TestCase(null)]
	public void A_malformed_topic_is_invalid(string? topic)
		=> Assert.That(MessageTopic.IsValidTopic(topic), Is.False);

	[Test]
	public void A_topic_longer_than_the_limit_is_invalid()
		=> Assert.That(MessageTopic.IsValidTopic("a." + new string('b', MessageTopic.MaxLength)), Is.False);

	[TestCase("obs.*")]
	[TestCase("obs.scene.*")]
	[TestCase("obs.scene.changed")]
	public void A_topic_or_a_prefix_wildcard_is_a_valid_pattern(string pattern)
		=> Assert.That(MessageTopic.IsValidPattern(pattern), Is.True);

	[TestCase("*")]
	[TestCase("obs")]
	[TestCase("obs.*.changed")]
	[TestCase("obs*")]
	[TestCase("obs.**")]
	public void Anything_else_is_not_a_valid_pattern(string pattern)
		=> Assert.That(MessageTopic.IsValidPattern(pattern), Is.False);

	[TestCase("obs.*", "obs.scene.changed", true)]
	[TestCase("obs.*", "obs.stream", true)]
	[TestCase("obs.*", "obsidian.note", false)]
	[TestCase("obs.scene.*", "obs.scene", false)]
	[TestCase("obs.scene.changed", "obs.scene.changed", true)]
	[TestCase("obs.scene.changed", "obs.scene.changed.late", false)]
	public void A_pattern_matches_its_topic_and_everything_below_its_prefix(string pattern, string topic, bool matches)
		=> Assert.That(MessageTopic.Matches(pattern, topic), Is.EqualTo(matches));
}
