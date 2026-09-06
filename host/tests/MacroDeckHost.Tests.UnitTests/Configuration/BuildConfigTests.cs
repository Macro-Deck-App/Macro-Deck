using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Tests.UnitTests.Configuration;

public class BuildConfigTests
{
	[Test]
	public void Defaults_match_the_selected_build_channel()
	{
		if (BuildConfig.Channel == BuildChannel.Development)
		{
			Assert.Multiple(() =>
			{
				Assert.That(BuildConfig.DefaultPublicPort, Is.EqualTo(7193));
				Assert.That(BuildConfig.DefaultPublicHttpsPort, Is.EqualTo(7194));
				Assert.That(BuildConfig.DataRootDirectoryName, Is.EqualTo(".data"));
				Assert.That(BuildConfig.LoopbackPortFileName, Is.EqualTo("macro-deck-host-development.port"));
				Assert.That(BuildConfig.ApplicationDisplayName, Is.EqualTo("Macro Deck Development"));
			});
			return;
		}

		Assert.Multiple(() =>
		{
			Assert.That(BuildConfig.Channel, Is.EqualTo(BuildChannel.Production));
			Assert.That(BuildConfig.DefaultPublicPort, Is.EqualTo(8193));
			Assert.That(BuildConfig.DefaultPublicHttpsPort, Is.EqualTo(8194));
			Assert.That(BuildConfig.DataRootDirectoryName, Is.EqualTo("MacroDeck"));
			Assert.That(BuildConfig.LoopbackPortFileName, Is.EqualTo("macro-deck-host.port"));
			Assert.That(BuildConfig.ApplicationDisplayName, Is.EqualTo("Macro Deck"));
		});
	}

	[Test]
	public void Build_environment_reports_the_generated_channel()
	{
		Assert.That(new BuildEnvironment().Channel, Is.EqualTo(BuildConfig.Channel));
	}

	[Test]
	public void ResolvePublicPort_uses_a_valid_override()
	{
		Assert.That(BuildConfig.ResolvePublicPort("45123"), Is.EqualTo(45123));
	}

	[Test]
	public void ResolvePublicPort_uses_the_default_for_an_invalid_override()
	{
		Assert.Multiple(() =>
		{
			Assert.That(BuildConfig.ResolvePublicPort(null), Is.EqualTo(BuildConfig.DefaultPublicPort));
			Assert.That(BuildConfig.ResolvePublicPort("0"), Is.EqualTo(BuildConfig.DefaultPublicPort));
			Assert.That(BuildConfig.ResolvePublicPort("70000"), Is.EqualTo(BuildConfig.DefaultPublicPort));
			Assert.That(BuildConfig.ResolvePublicPort("invalid"), Is.EqualTo(BuildConfig.DefaultPublicPort));
		});
	}

	[Test]
	public void Host_endpoints_use_the_generated_configuration()
	{
		Assert.Multiple(() =>
		{
			Assert.That(HostEndpoints.PublicPort, Is.EqualTo(BuildConfig.PublicPort));
			Assert.That(Path.GetFileName(HostEndpoints.LoopbackPortFilePath),
				Is.EqualTo(BuildConfig.LoopbackPortFileName));
		});
	}

	[Test]
	public void Loopback_port_uses_valid_overrides_and_channel_defaults()
	{
		var channelDefault = BuildConfig.Channel == BuildChannel.Development
			? HostEndpoints.DevelopmentLoopbackPort
			: 0;

		Assert.Multiple(() =>
		{
			Assert.That(HostEndpoints.ResolveLoopbackPort("45123"), Is.EqualTo(45123));
			Assert.That(HostEndpoints.ResolveLoopbackPort("invalid"), Is.EqualTo(channelDefault));
		});
	}
}
