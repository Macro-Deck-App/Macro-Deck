namespace MacroDeck.Localization.Tests.UnitTests;

/// <summary>
/// The "plugin resources live in their own namespace so they cannot overwrite Macro Deck's or another
/// plugin's" acceptance criterion, and the atomic register/replace/remove lifecycle around it.
/// </summary>
[TestFixture]
public class ScopeIsolationTests
{
	private const string _spotify = "com.example.spotify";
	private const string _obs = "com.example.obs";

	private static LocalizationCatalog Catalog(string scope, params (string Key, string Value)[] entries)
	{
		var templates = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var entry in entries)
		{
			templates[entry.Key] = entry.Value;
		}

		return new LocalizationCatalog(scope,
			"en",
			new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
			{
				["en"] = templates,
			});
	}

	[Test]
	public void Two_plugins_and_Macro_Deck_can_all_define_the_same_short_key()
	{
		var registry = new LocalizationCatalogRegistry();
		registry.Register(Catalog(LocalizationScope.ForPlugin(_spotify), ("Connect", "Connect to Spotify")));
		registry.Register(Catalog(LocalizationScope.ForPlugin(_obs), ("Connect", "Connect to OBS")));
		registry.Register(Catalog(LocalizationScope.MacroDeck, ("Connect", "Connect")));

		var resolver = new LocalizationResolver(registry);

		Assert.Multiple(() =>
		{
			Assert.That(resolver.Resolve(new LocalizedString(LocalizationKey.Plugin(_spotify, "Connect")), "en"),
				Is.EqualTo("Connect to Spotify"));
			Assert.That(resolver.Resolve(new LocalizedString(LocalizationKey.Plugin(_obs, "Connect")), "en"),
				Is.EqualTo("Connect to OBS"));
			Assert.That(resolver.Resolve(new LocalizedString(LocalizationKey.MacroDeck("Connect")), "en"),
				Is.EqualTo("Connect"));
		});
	}

	// The scenario a cosmetic-prefix implementation fails and every single-plugin test passes: asking a
	// plugin that ships no such key must not fall through to whoever does.
	[Test]
	public void A_plugin_without_a_key_never_borrows_another_plugins_text()
	{
		var registry = new LocalizationCatalogRegistry();
		registry.Register(Catalog(LocalizationScope.ForPlugin(_spotify), ("Connect", "Connect to Spotify")));
		registry.Register(Catalog(LocalizationScope.ForPlugin(_obs), ("Disconnect", "Disconnect from OBS")));
		registry.Register(Catalog(LocalizationScope.MacroDeck, ("Connect", "Connect")));

		var resolved = new LocalizationResolver(registry)
			.Resolve(new LocalizedString(LocalizationKey.Plugin(_obs, "Connect")), "en");

		Assert.That(resolved, Is.EqualTo("[[plugin:com.example.obs:Connect]]"));
	}

	[Test]
	public void Unregistering_a_plugin_takes_its_scope_with_it()
	{
		var registry = new LocalizationCatalogRegistry();
		registry.Register(Catalog(LocalizationScope.ForPlugin(_spotify), ("Connect", "Connect to Spotify")));

		var resolver = new LocalizationResolver(registry);
		var key = new LocalizedString(LocalizationKey.Plugin(_spotify, "Connect"));

		Assert.That(resolver.Resolve(key, "en"), Is.EqualTo("Connect to Spotify"));

		registry.Unregister(LocalizationScope.ForPlugin(_spotify));

		Assert.That(resolver.Resolve(key, "en"), Is.EqualTo("[[plugin:com.example.spotify:Connect]]"));
	}

	// What distinguishes an atomic replacement from unregister-then-register: a snapshot taken before the
	// update still answers with the version it was taken from, so a reader mid-render never sees a scope
	// that is half one version and half the next.
	[Test]
	public void Replacing_a_catalog_swaps_it_whole_rather_than_mutating_it()
	{
		var scope = LocalizationScope.ForPlugin(_spotify);
		var registry = new LocalizationCatalogRegistry();
		registry.Register(Catalog(scope, ("A", "one"), ("B", "two")));

		var before = registry.Find(scope);

		registry.Register(Catalog(scope, ("A", "eins"), ("C", "drei")));

		var after = registry.Find(scope);

		Assert.Multiple(() =>
		{
			Assert.That(before!.TryGetTemplate("en", "B", out var stillThere), Is.True);
			Assert.That(stillThere, Is.EqualTo("two"));
			Assert.That(before.TryGetTemplate("en", "C", out _), Is.False);

			Assert.That(after!.TryGetTemplate("en", "A", out var replaced), Is.True);
			Assert.That(replaced, Is.EqualTo("eins"));
			Assert.That(after.TryGetTemplate("en", "B", out _), Is.False);
		});
	}

	[Test]
	public void A_scope_that_was_never_registered_is_simply_absent()
	{
		var registry = new LocalizationCatalogRegistry();

		Assert.Multiple(() =>
		{
			Assert.That(registry.Find(LocalizationScope.ForPlugin(_spotify)), Is.Null);
			Assert.That(registry.Unregister(LocalizationScope.ForPlugin(_spotify)), Is.False);
			Assert.That(registry.Scopes, Is.Empty);
		});
	}
}
