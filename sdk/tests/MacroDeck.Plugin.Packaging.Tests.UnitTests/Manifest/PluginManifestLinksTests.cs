using System.Text.Json;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Packaging.Tests.UnitTests.Manifest;

[TestFixture]
internal sealed class PluginManifestLinksTests
{
	private const string IssueExample = """
										[
										  { "type": "documentation", "url": "https://docs.example.com" },
										  { "type": "wiki", "url": "https://github.com/example/example-plugin/wiki" },
										  { "type": "issues", "url": "https://github.com/example/example-plugin/issues" },
										  { "type": "support", "url": "https://example.com/support" },
										  { "type": "community", "url": "https://discord.gg/example" },
										  { "type": "custom", "label": "Setup Guide", "url": "https://example.com/setup" }
										]
										""";

	private static IReadOnlyList<PluginManifestLinkProblem> Validate(string json)
	{
		using var document = JsonDocument.Parse(json);
		return PluginManifestLinks.Validate(document.RootElement);
	}

	private static IReadOnlyList<PluginManifestLink> Displayable(string json)
	{
		using var document = JsonDocument.Parse(json);
		return PluginManifestLinks.Displayable(document.RootElement);
	}

	[Test]
	public void The_example_from_the_feature_request_is_valid()
	{
		Assert.That(Validate(IssueExample), Is.Empty);
	}

	[Test]
	public void Every_standard_type_is_valid_without_a_label()
	{
		var links = string.Join(",",
			PluginManifestLinkTypes.Standard.Select(type => $$"""{ "type": "{{type}}", "url": "https://example.com/{{type}}" }"""));

		Assert.That(Validate($"[{links}]"), Is.Empty);
	}

	[TestCase("""[{ "url": "https://example.com" }]""", 0, "type")]
	[TestCase("""[{ "type": "  ", "url": "https://example.com" }]""", 0, "type")]
	[TestCase("""[{ "type": 5, "url": "https://example.com" }]""", 0, "type")]
	[TestCase("""[{ "type": "wiki" }]""", 0, "url")]
	[TestCase("""[{ "type": "wiki", "url": "ftp://example.com" }]""", 0, "url")]
	[TestCase("""[{ "type": "wiki", "url": "example.com/wiki" }]""", 0, "url")]
	[TestCase("""[{ "type": "wiki", "url": "javascript:alert(1)" }]""", 0, "url")]
	[TestCase("""[{ "type": "custom", "url": "https://example.com" }]""", 0, "label")]
	[TestCase("""[{ "type": "custom", "label": null, "url": "https://example.com" }]""", 0, "label")]
	[TestCase("""[{ "type": "custom", "label": "   ", "url": "https://example.com" }]""", 0, "label")]
	[TestCase("""[{ "type": "custom", "label": "Line\nbreak", "url": "https://example.com" }]""", 0, "label")]
	[TestCase("""[{ "type": "wiki", "label": "My wiki", "url": "https://example.com" }]""", 0, "label")]
	[TestCase("""[{ "type": "wiki", "label": "", "url": "https://example.com" }]""", 0, "label")]
	[TestCase("""[null]""", 0, null)]
	[TestCase("""["https://example.com"]""", 0, null)]
	public void An_invalid_link_is_one_error_at_its_index_and_property(string json, int index, string? property)
	{
		var problems = Validate(json);

		Assert.That(problems, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(problems[0].Severity, Is.EqualTo(PluginManifestLinkProblemSeverity.Error));
			Assert.That(problems[0].Index, Is.EqualTo(index));
			Assert.That(problems[0].Property, Is.EqualTo(property));
		});
	}

	[Test]
	public void A_value_that_is_not_an_array_is_an_error_and_null_means_no_links()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Validate("""{ "type": "wiki" }"""), Has.Count.EqualTo(1));
			Assert.That(Validate("null"), Is.Empty);
		});
	}

	[Test]
	public void A_standard_link_may_carry_a_null_label()
	{
		Assert.That(Validate("""[{ "type": "wiki", "label": null, "url": "https://example.com" }]"""), Is.Empty);
	}

	[Test]
	public void A_custom_label_may_be_128_characters_but_not_129()
	{
		string Link(int length) => $$"""[{ "type": "custom", "label": "{{new string('a', length)}}", "url": "https://example.com" }]""";

		Assert.Multiple(() =>
		{
			Assert.That(Validate(Link(128)), Is.Empty);
			Assert.That(Validate(Link(129)), Has.Count.EqualTo(1));
		});
	}

	[TestCase("""{ "type": "wiki", "url": "https://example.com/a" }""", """{ "type": "support", "url": "https://example.com/a" }""", "url")]
	[TestCase("""{ "type": "wiki", "url": "https://EXAMPLE.com/a" }""", """{ "type": "support", "url": "https://example.com/a" }""", "url")]
	[TestCase("""{ "type": "community", "url": "https://discord.gg/a" }""", """{ "type": "community", "url": "https://reddit.com/r/a" }""", "type")]
	[TestCase("""{ "type": "custom", "label": "Guide", "url": "https://example.com/a" }""", """{ "type": "custom", "label": " guide ", "url": "https://example.com/b" }""", "label")]
	public void A_second_link_repeating_a_url_a_standard_type_or_a_custom_label_is_a_duplicate(string first,
		string second,
		string property)
	{
		var problems = Validate($"[{first},{second}]");

		Assert.That(problems, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(problems[0].Index, Is.EqualTo(1));
			Assert.That(problems[0].Property, Is.EqualTo(property));
		});
	}

	[Test]
	public void Urls_that_differ_only_in_their_fragment_are_different_links()
	{
		Assert.That(Validate("""
							 [
							   { "type": "documentation", "url": "https://example.com/docs#setup" },
							   { "type": "support", "url": "https://example.com/docs#contact" }
							 ]
							 """),
			Is.Empty);
	}

	[Test]
	public void An_unknown_type_is_only_a_warning()
	{
		var problems = Validate("""[{ "type": "roadmap", "url": "https://example.com/roadmap" }]""");

		Assert.That(problems, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(problems[0].Severity, Is.EqualTo(PluginManifestLinkProblemSeverity.Warning));
			Assert.That(problems[0].Property, Is.EqualTo("type"));
		});
	}

	[Test]
	public void Displayable_keeps_valid_links_in_order_and_drops_invalid_unknown_and_repeated_ones()
	{
		var links = Displayable("""
								[
								  { "type": "issues", "url": "https://example.com/issues" },
								  { "type": "wiki", "url": "ftp://example.com/wiki" },
								  { "type": "roadmap", "url": "https://example.com/roadmap" },
								  null,
								  { "type": "custom", "label": "  Setup Guide ", "url": "https://example.com/setup" },
								  { "type": "issues", "url": "https://example.com/other-issues" },
								  { "type": "support", "url": "https://example.com/issues" },
								  { "type": "donate", "label": "Buy me a coffee", "url": "https://example.com/donate" },
								  { "type": "documentation", "url": "https://example.com/docs" }
								]
								""");

		Assert.That(links,
			Is.EqualTo(new[]
			{
				new PluginManifestLink { Type = "issues", Url = "https://example.com/issues" },
				new PluginManifestLink { Type = "custom", Url = "https://example.com/setup", Label = "Setup Guide" },
				new PluginManifestLink { Type = "documentation", Url = "https://example.com/docs" }
			}));
	}

	[Test]
	public void Displayable_of_anything_but_an_array_is_empty()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Displayable("""{ "type": "wiki" }"""), Is.Empty);
			Assert.That(Displayable("\"https://example.com\""), Is.Empty);
		});
	}
}
