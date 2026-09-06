namespace MacroDeck.Localization.Tests.UnitTests;

/// <summary>
/// The "missing translations have deterministic fallback behaviour" acceptance criterion.
///
/// <para>
/// Every rung of the chain carries a <b>different</b> sentinel, which is what makes the test able to
/// fail. With the same text on each rung, an implementation that falls straight through to the default
/// language - or one with no fallback at all - produces exactly the same answers as a correct one.
/// </para>
/// </summary>
[TestFixture]
public class FallbackChainTests
{
	private const string _key = "Greeting";
	private const string _pluginDefaultCulture = "fr";

	/// <summary>Builds a resolver whose catalog carries only <paramref name="cultures" />, so a rung can be
	/// removed and the next one observed.</summary>
	private static LocalizationResolver Resolver(params string[] cultures)
	{
		var sentinels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["de-DE"] = "A",
			["de"] = "B",
			[_pluginDefaultCulture] = "C",
			["en"] = "D",
		};

		var templates = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

		foreach (var culture in cultures)
		{
			templates[culture] = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				[_key] = sentinels[culture],
			};
		}

		var registry = new LocalizationCatalogRegistry();
		registry.Register(new LocalizationCatalog(LocalizationScope.ForPlugin("com.example.test"),
			_pluginDefaultCulture,
			templates));

		return new LocalizationResolver(registry);
	}

	private static LocalizedString Greeting()
		=> new(LocalizationKey.Plugin("com.example.test", _key));

	[Test]
	public void The_requested_culture_wins_when_it_is_present()
		=> Assert.That(Resolver("de-DE", "de", _pluginDefaultCulture, "en").Resolve(Greeting(), "de-DE"),
			Is.EqualTo("A"));

	[Test]
	public void The_neutral_culture_comes_before_the_default_language()
		=> Assert.That(Resolver("de", _pluginDefaultCulture, "en").Resolve(Greeting(), "de-DE"),
			Is.EqualTo("B"));

	[Test]
	public void The_catalogs_own_default_language_comes_before_Macro_Decks()
		=> Assert.That(Resolver(_pluginDefaultCulture, "en").Resolve(Greeting(), "de-DE"),
			Is.EqualTo("C"));

	[Test]
	public void Macro_Decks_default_language_is_the_last_culture_tried()
		=> Assert.That(Resolver("en").Resolve(Greeting(), "de-DE"), Is.EqualTo("D"));

	[Test]
	public void A_regional_variant_of_the_same_language_falls_back_to_its_neutral_culture()
		=> Assert.That(Resolver("de", _pluginDefaultCulture, "en").Resolve(Greeting(), "de-CH"),
			Is.EqualTo("B"));

	// The chain walks up to the neutral culture, never sideways: de-AT is not a fallback for de-DE, or a
	// reader would silently get Austrian wording for a German request.
	[Test]
	public void A_sibling_regional_culture_is_never_used()
	{
		var templates = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
		{
			["de-AT"] = new Dictionary<string, string>(StringComparer.Ordinal) { [_key] = "E" },
			[_pluginDefaultCulture] = new Dictionary<string, string>(StringComparer.Ordinal) { [_key] = "C" },
		};

		var registry = new LocalizationCatalogRegistry();
		registry.Register(new LocalizationCatalog(LocalizationScope.ForPlugin("com.example.test"),
			_pluginDefaultCulture,
			templates));

		Assert.That(new LocalizationResolver(registry).Resolve(Greeting(), "de-DE"), Is.EqualTo("C"));
	}

	[Test]
	public void A_key_no_culture_carries_resolves_to_a_conspicuous_placeholder()
	{
		var resolved = Resolver().Resolve(Greeting(), "de-DE");

		Assert.Multiple(() =>
		{
			// Pinned exactly: an empty string here would read as a rendering bug rather than a missing
			// translation, which is the one thing the placeholder exists to prevent.
			Assert.That(resolved, Is.EqualTo("[[plugin:com.example.test:Greeting]]"));
			Assert.That(resolved, Is.Not.Empty);
		});
	}

	[Test]
	public void A_key_in_a_scope_nothing_registered_resolves_rather_than_throwing()
	{
		var resolver = new LocalizationResolver(new LocalizationCatalogRegistry());

		Assert.That(resolver.Resolve(Greeting(), "de-DE"),
			Is.EqualTo("[[plugin:com.example.test:Greeting]]"));
	}
}
