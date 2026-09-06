using MacroDeck.Plugin.Protocol.Auth;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Auth;

[TestFixture]
public class PluginAuthDefaultsTests
{
	[Test]
	public void Header_names_match_the_frozen_wire_values()
	{
		Assert.Multiple(() =>
		{
			Assert.That(PluginAuthDefaults.PluginIdHeaderName, Is.EqualTo("X-MacroDeck-Plugin-Id"));
			Assert.That(PluginAuthDefaults.PluginSecretHeaderName, Is.EqualTo("X-MacroDeck-Plugin-Secret"));
			Assert.That(PluginAuthDefaults.EnrollmentTokenHeaderName, Is.EqualTo("X-MacroDeck-Enrollment-Token"));
			Assert.That(PluginAuthDefaults.SessionIdHeaderName, Is.EqualTo("X-MacroDeck-Session-Id"));
			Assert.That(PluginAuthDefaults.AuthorizationHeaderName, Is.EqualTo("Authorization"));
		});
	}

	[Test]
	public void Scope_and_bearer_scheme_values_are_frozen()
	{
		Assert.Multiple(() =>
		{
			Assert.That(PluginAuthDefaults.PluginScope, Is.EqualTo("plugin"));
			Assert.That(PluginAuthDefaults.BearerScheme, Is.EqualTo("Bearer"));
		});
	}

	[Test]
	public void Session_token_lifetime_mirrors_the_hosts_fifteen_minute_access_token_lifetime()
		=> Assert.That(PluginAuthDefaults.SessionTokenLifetime, Is.EqualTo(TimeSpan.FromMinutes(15)));

	[Test]
	public void Min_plugin_secret_length_is_thirty_two_random_bytes_base64_url_encoded()
		=> Assert.That(PluginAuthDefaults.MinPluginSecretLength, Is.EqualTo(43));

	[Test]
	public void All_lists_every_header_name_exactly_once()
	{
		Assert.Multiple(() =>
		{
			Assert.That(PluginAuthDefaults.All, Has.Count.EqualTo(5));
			Assert.That(PluginAuthDefaults.All, Is.Unique);
			Assert.That(PluginAuthDefaults.All, Does.Contain(PluginAuthDefaults.PluginIdHeaderName));
			Assert.That(PluginAuthDefaults.All, Does.Contain(PluginAuthDefaults.PluginSecretHeaderName));
			Assert.That(PluginAuthDefaults.All, Does.Contain(PluginAuthDefaults.EnrollmentTokenHeaderName));
			Assert.That(PluginAuthDefaults.All, Does.Contain(PluginAuthDefaults.SessionIdHeaderName));
			Assert.That(PluginAuthDefaults.All, Does.Contain(PluginAuthDefaults.AuthorizationHeaderName));
		});
	}

	[Test]
	public void The_plugin_secret_header_and_the_session_id_header_are_distinct_constants()
	{
		// The "secret header is session-exchange-only, never on the upgrade" rule is enforced by
		// #107's transport code; this contract can only pin the constants apart from each other.
		Assert.That(PluginAuthDefaults.PluginSecretHeaderName, Is.Not.EqualTo(PluginAuthDefaults.SessionIdHeaderName));
	}
}
