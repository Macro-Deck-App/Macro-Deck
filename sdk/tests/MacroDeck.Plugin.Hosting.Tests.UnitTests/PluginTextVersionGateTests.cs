using MacroDeck.Localization;
using MacroDeck.Plugin.Hosting.Localization;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>
/// A plugin's descriptor text may only cross the wire as a reference once the handshake settled on a
/// version whose host understands the shape. Below that the plugin resolves it itself, in the language
/// its author wrote it in, because an older host would reject an object where it expects a string.
/// </summary>
[TestFixture]
public class PluginTextVersionGateTests
{
	private const string PluginId = "com.example.spotify";

	private static readonly LocalizationKey _play = new(LocalizationScope.ForPlugin(PluginId), "Actions.Play");

	[SetUp]
	public void SetUp()
	{
		PluginText.Use(new LocalizationCatalog(LocalizationScope.ForPlugin(PluginId),
			"de",
			new Dictionary<string, IReadOnlyDictionary<string, string>>
			{
				["de"] = new Dictionary<string, string> { ["Actions.Play"] = "Wiedergabe starten" },
				["en"] = new Dictionary<string, string> { ["Actions.Play"] = "Start playback" }
			}));
	}

	[TearDown]
	public void TearDown() => PluginText.Negotiated(ProtocolVersions.Minimum);

	[Test]
	public void A_reference_travels_as_a_reference_once_the_shape_is_negotiated()
	{
		PluginText.Negotiated(ProtocolVersions.LocalizedDescriptors);

		var wire = PluginText.ToWire(new LocalizedString(_play));

		// The point of the whole exercise: what leaves the plugin still names the key, so the client
		// that renders it picks the language of whoever is reading - not of whoever wrote the plugin.
		Assert.Multiple(() =>
		{
			Assert.That(wire.IsLocalized, Is.True);
			Assert.That(wire.Localized!.Value.Key, Is.EqualTo(_play));
		});
	}

	[Test]
	public void A_reference_is_resolved_in_the_authors_language_below_the_negotiated_shape()
	{
		PluginText.Negotiated(ProtocolVersions.LocalizedDescriptors - 1);

		var wire = PluginText.ToWire(new LocalizedString(_play));

		// The catalog's own default is German here, deliberately: the fallback is the author's language,
		// not English, and a test that used an English-default catalog could not tell the two apart.
		Assert.Multiple(() =>
		{
			Assert.That(wire.IsLocalized, Is.False);
			Assert.That(wire.Literal, Is.EqualTo("Wiedergabe starten"));
		});
	}

	[Test]
	public void A_literal_is_untouched_at_every_version()
	{
		foreach (var version in ProtocolVersions.Supported)
		{
			PluginText.Negotiated(version);

			Assert.That(PluginText.ToWire(LocalizedText.FromLiteral("Play")).Literal,
				Is.EqualTo("Play"),
				$"a plain string must survive protocol v{version} unchanged");
		}
	}

	[Test]
	public void An_unset_property_stays_unset_rather_than_becoming_empty_text()
	{
		PluginText.Negotiated(ProtocolVersions.LocalizedDescriptors);

		Assert.That(PluginText.ToWireOrNull(default), Is.Null);
	}
}
