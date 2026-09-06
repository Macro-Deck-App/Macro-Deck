namespace MacroDeck.Localization.Tests.UnitTests;

/// <summary>
/// How an argument looks once substituted. Pinned as exact strings because the TypeScript formatter is a
/// line-for-line mirror of this one and the two have to agree character for character - the acceptance
/// criterion is that C# and TypeScript are validated from the same resource definitions, and a locale
/// separator or a capitalised boolean appearing on one side only is exactly how that quietly stops being
/// true.
/// </summary>
[TestFixture]
public class FormattingTests
{
	private const string _scope = "macrodeck";

	private static string Format(string template, params (string Name, object? Value)[] arguments)
	{
		var registry = new LocalizationCatalogRegistry();
		registry.Register(new LocalizationCatalog(_scope,
			"en",
			new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
			{
				["en"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Key"] = template },
			}));

		var values = new Dictionary<string, object?>(StringComparer.Ordinal);

		foreach (var argument in arguments)
		{
			values[argument.Name] = argument.Value;
		}

		return new LocalizationResolver(registry)
			.Resolve(new LocalizedString(LocalizationKey.MacroDeck("Key"), values), "en");
	}

	[Test]
	public void A_named_placeholder_is_substituted()
		=> Assert.That(Format("Connected as {userName}", ("userName", "ada")),
			Is.EqualTo("Connected as ada"));

	// Named, not positional, so a translation may reorder its placeholders freely.
	[Test]
	public void Placeholders_may_appear_in_any_order()
		=> Assert.That(Format("{second} then {first}", ("first", "a"), ("second", "b")),
			Is.EqualTo("b then a"));

	[Test]
	public void A_doubled_brace_is_a_literal_brace()
		=> Assert.That(Format("{{literal}} and {value}", ("value", "x")),
			Is.EqualTo("{literal} and x"));

	// Visible on purpose: a blank where a device name belongs reads as a rendering bug, whereas the
	// placeholder itself names the argument that was not supplied.
	[Test]
	public void An_argument_with_no_value_is_left_as_its_placeholder()
		=> Assert.That(Format("Connected as {userName}"), Is.EqualTo("Connected as {userName}"));

	[TestCase(true, "true")]
	[TestCase(false, "false")]
	public void A_boolean_is_lowercase(bool value, string expected)
		=> Assert.That(Format("{value}", ("value", value)), Is.EqualTo(expected));

	[TestCase(1234, "1234")]
	[TestCase(-7, "-7")]
	public void An_integer_carries_no_thousands_separator(int value, string expected)
		=> Assert.That(Format("{value}", ("value", value)), Is.EqualTo(expected));

	[TestCase(1.5, "1.5")]
	[TestCase(2.0, "2")]
	public void A_number_uses_a_dot_regardless_of_the_readers_language(double value, string expected)
		=> Assert.That(Format("{value}", ("value", value)), Is.EqualTo(expected));

	// The one shape .NET and JavaScript spell differently by default - 1E+21 against 1e+21 - normalised
	// so the two formatters cannot disagree on it.
	[Test]
	public void A_number_in_exponent_form_uses_a_lowercase_exponent()
		=> Assert.That(Format("{value}", ("value", 1e21)), Is.EqualTo("1e+21"));

	// "{field} is required" where {field} is itself a localized label: resolving the outer message has to
	// resolve the inner one too, or a German reader gets "API key ist erforderlich".
	[Test]
	public void A_localized_argument_is_resolved_in_the_same_language()
	{
		var registry = new LocalizationCatalogRegistry();
		registry.Register(new LocalizationCatalog(_scope,
			"en",
			new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
			{
				["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["Required"] = "{field} is required",
					["ApiKey"] = "API key",
				},
				["de"] = new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["Required"] = "{field} ist erforderlich",
					["ApiKey"] = "API-Schlüssel",
				},
			}));

		var message = new LocalizedString(LocalizationKey.MacroDeck("Required"),
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["field"] = LocalizedText.FromLocalized(new LocalizedString(LocalizationKey.MacroDeck("ApiKey"))),
			});

		var resolver = new LocalizationResolver(registry);

		Assert.Multiple(() =>
		{
			Assert.That(resolver.Resolve(message, "en"), Is.EqualTo("API key is required"));
			Assert.That(resolver.Resolve(message, "de"), Is.EqualTo("API-Schlüssel ist erforderlich"));
		});
	}
}
