namespace MacroDeck.Localization.Tests.UnitTests;

/// <summary>
/// The "count-dependent wording goes through the localization system" acceptance criterion of issue #680.
///
/// <para>
/// A plural family is stored as its forms and referenced by its base key, so the behaviour worth pinning
/// is the selection itself: which form a count picks, that the selection happens per candidate culture
/// rather than after the whole chain, and that a family with only <c>Other</c> still answers every count.
/// </para>
/// </summary>
[TestFixture]
public class PluralResolutionTests
{
	private const string _pluginId = "com.example.test";
	private const string _key = "Icons";

	private static LocalizedString Icons(object? count)
		=> new(LocalizationKey.Plugin(_pluginId, _key),
			count is null
				? new Dictionary<string, object?>(StringComparer.Ordinal)
				: new Dictionary<string, object?>(StringComparer.Ordinal) { ["count"] = count });

	private static LocalizationResolver Resolver(
		params (string Culture, (string Key, string Template)[] Entries)[] cultures)
	{
		var templates = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

		foreach (var culture in cultures)
		{
			var byKey = new Dictionary<string, string>(StringComparer.Ordinal);

			foreach (var entry in culture.Entries)
			{
				byKey[entry.Key] = entry.Template;
			}

			templates[culture.Culture] = byKey;
		}

		var registry = new LocalizationCatalogRegistry();
		registry.Register(new LocalizationCatalog(LocalizationScope.ForPlugin(_pluginId), "en", templates));

		return new LocalizationResolver(registry);
	}

	private static LocalizationResolver English()
		=> Resolver(("en", [("Icons.One", "{count} icon"), ("Icons.Other", "{count} icons")]));

	[TestCase(1, "1 icon")]
	[TestCase(0, "0 icons")]
	[TestCase(2, "2 icons")]
	[TestCase(17, "17 icons")]
	public void The_count_selects_the_form(int count, string expected)
		=> Assert.That(English().Resolve(Icons(count), "en"), Is.EqualTo(expected));

	[Test]
	public void A_count_of_one_in_another_numeric_type_still_selects_the_singular()
		=> Assert.That(English().Resolve(Icons(1L), "en"), Is.EqualTo("1 icon"));

	[Test]
	public void A_family_with_only_the_other_form_answers_every_count()
	{
		var resolver = Resolver(("en", [("Icons.Other", "{count} icons")]));

		Assert.Multiple(() =>
		{
			Assert.That(resolver.Resolve(Icons(1), "en"), Is.EqualTo("1 icons"));
			Assert.That(resolver.Resolve(Icons(4), "en"), Is.EqualTo("4 icons"));
		});
	}

	[Test]
	public void A_reference_carrying_no_count_does_not_resolve_to_a_form()
		=> Assert.That(English().Resolve(Icons(null), "en"), Is.EqualTo($"[[plugin:{_pluginId}:{_key}]]"));

	/// <summary>
	/// The regression the per-culture probe exists for: German carries both forms, so a German reader must
	/// never be answered with the English singular just because English was probed for the same form first.
	/// </summary>
	[Test]
	public void A_culture_that_carries_the_family_is_never_answered_from_the_fallback()
	{
		var resolver = Resolver(("de", [("Icons.One", "{count} Symbol"), ("Icons.Other", "{count} Symbole")]),
			("en", [("Icons.One", "{count} icon"), ("Icons.Other", "{count} icons")]));

		Assert.Multiple(() =>
		{
			Assert.That(resolver.Resolve(Icons(1), "de"), Is.EqualTo("1 Symbol"));
			Assert.That(resolver.Resolve(Icons(3), "de"), Is.EqualTo("3 Symbole"));
		});
	}

	/// <summary>
	/// A form the requested culture is missing falls through to the next culture rather than to the other
	/// form of the same language - a half-translated family reads as the wrong language, not as the wrong
	/// number.
	/// </summary>
	[Test]
	public void A_missing_form_falls_back_to_the_next_culture()
	{
		var resolver = Resolver(("de", [("Icons.Other", "{count} Symbole")]),
			("en", [("Icons.One", "{count} icon"), ("Icons.Other", "{count} icons")]));

		Assert.That(resolver.Resolve(Icons(1), "de"), Is.EqualTo("1 Symbole"));
	}

	[Test]
	public void A_form_may_leave_the_count_out_of_its_text()
	{
		var resolver = Resolver(("en", [("Icons.One", "one icon"), ("Icons.Other", "{count} icons")]));

		Assert.Multiple(() =>
		{
			Assert.That(resolver.Resolve(Icons(1), "en"), Is.EqualTo("one icon"));
			Assert.That(resolver.Resolve(Icons(6), "en"), Is.EqualTo("6 icons"));
		});
	}
}
