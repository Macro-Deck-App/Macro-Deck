using MacroDeckHost.Application.Configuration;

namespace MacroDeckHost.Tests.UnitTests.Configuration;

public class PublicPortSelectorTests
{
	private const int LoopbackPort = 51234;
	private const string LoopbackPortValue = "51234";

	[Test]
	public void The_environment_override_wins_over_a_configured_port()
	{
		var selection = PublicPortSelector.Resolve("9100", "9200", LoopbackPort);

		Assert.Multiple(() =>
		{
			Assert.That(selection.Port, Is.EqualTo(9100));
			Assert.That(selection.Source, Is.EqualTo(PublicPortSource.Environment));
		});
	}

	[Test]
	public void The_configured_port_is_used_without_an_override()
	{
		var selection = PublicPortSelector.Resolve(null, "9200", LoopbackPort);

		Assert.Multiple(() =>
		{
			Assert.That(selection.Port, Is.EqualTo(9200));
			Assert.That(selection.Source, Is.EqualTo(PublicPortSource.Preference));
		});
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("abc")]
	[TestCase("0")]
	[TestCase("80")]
	[TestCase("1023")]
	[TestCase("70000")]
	public void An_unusable_configured_port_falls_back_to_the_build_default(string? configured)
	{
		var selection = PublicPortSelector.Resolve(null, configured, LoopbackPort);

		Assert.Multiple(() =>
		{
			Assert.That(selection.Port, Is.EqualTo(BuildConfig.DefaultPublicPort));
			Assert.That(selection.Source, Is.EqualTo(PublicPortSource.Default));
		});
	}

	[Test]
	public void A_configured_port_colliding_with_the_loopback_listener_falls_back()
	{
		var selection = PublicPortSelector.Resolve(null, LoopbackPortValue, LoopbackPort);

		Assert.Multiple(() =>
		{
			Assert.That(selection.Port, Is.EqualTo(BuildConfig.DefaultPublicPort));
			Assert.That(selection.Source, Is.EqualTo(PublicPortSource.Default));
		});
	}

	[Test]
	public void An_invalid_override_does_not_hide_a_valid_configured_port()
	{
		var selection = PublicPortSelector.Resolve("not-a-port", "9200", LoopbackPort);

		Assert.Multiple(() =>
		{
			Assert.That(selection.Port, Is.EqualTo(9200));
			Assert.That(selection.Source, Is.EqualTo(PublicPortSource.Preference));
		});
	}

	[Test]
	public void Privileged_ports_are_not_configurable_but_stay_valid_as_an_override()
	{
		Assert.Multiple(() =>
		{
			Assert.That(PublicPortSelector.IsConfigurable(80), Is.False);
			Assert.That(PublicPortSelector.IsConfigurable(1024), Is.True);
			Assert.That(PublicPortSelector.IsConfigurable(65535), Is.True);
			Assert.That(PublicPortSelector.IsConfigurable(65536), Is.False);
			Assert.That(PublicPortSelector.Resolve("80", null, LoopbackPort).Port, Is.EqualTo(80));
		});
	}
}
