using MacroDeck.Plugin.Cli.Scaffolding;
using MacroDeck.Plugin.Protocol.Handshake;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

[TestFixture]
public class PluginIdDerivationTests
{
	[Test]
	public void Derive_kebab_cases_the_name_under_com_example()
	{
		Assert.That(PluginIdDerivation.Derive("Spotify Controller"), Is.EqualTo("com.example.spotify-controller"));
	}

	[TestCase("Spotify Controller")]
	[TestCase("OBS-Studio Bridge")]
	[TestCase("3D Lights")]
	public void Every_derived_id_satisfies_PluginId_IsValid(string name)
	{
		var derived = PluginIdDerivation.Derive(name);

		Assert.That(derived, Is.Not.Null);
		Assert.That(PluginId.IsValid(derived), Is.True);
	}

	[Test]
	public void Derive_returns_null_for_a_blank_name()
	{
		Assert.That(PluginIdDerivation.Derive(""), Is.Null);
		Assert.That(PluginIdDerivation.Derive(null), Is.Null);
	}
}
