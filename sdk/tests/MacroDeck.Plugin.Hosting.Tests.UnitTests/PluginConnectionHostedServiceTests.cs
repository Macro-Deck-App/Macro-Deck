using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Handshake;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class PluginConnectionHostedServiceTests
{
	private static readonly PluginMetadata _metadata = new()
	{
		Id = "com.example.plugin",
		Name = "Example",
		Version = "1.2.3"
	};

	[Test]
	public void The_built_session_request_carries_the_plugins_configured_version()
	{
		var request = PluginConnectionHostedService.BuildSessionRequest([], _metadata, null);

		Assert.That(request.DeclaredVersion, Is.EqualTo("1.2.3"));
	}

	[Test]
	public void The_built_session_request_carries_the_sdk_usage_it_was_given()
	{
		var usage = new PluginSdkUsage
		{
			SdkVersion = "3.0.0",
			DeprecatedApis = ["M:MacroDeck.Sdk.Example.Old"],
			Truncated = true
		};

		var request = PluginConnectionHostedService.BuildSessionRequest([], _metadata, usage);

		Assert.Multiple(() =>
		{
			Assert.That(request.Sdk?.SdkVersion, Is.EqualTo("3.0.0"));
			Assert.That(request.Sdk?.DeprecatedApis, Is.EqualTo(new[] { "M:MacroDeck.Sdk.Example.Old" }));
			Assert.That(request.Sdk?.Truncated, Is.True);
		});
	}

	/// <summary>
	/// A plugin with nothing to report must send no SDK block at all rather than an empty one. The host
	/// reads an absent block as "unknown" and a present-but-empty deprecated-API list as "reported, and
	/// none used" - so sending an empty block here would claim a confirmation the plugin never made.
	/// </summary>
	[Test]
	public void The_built_session_request_omits_the_sdk_block_when_there_is_nothing_to_report()
	{
		var request = PluginConnectionHostedService.BuildSessionRequest([], _metadata, null);

		Assert.That(request.Sdk, Is.Null);
	}
}
