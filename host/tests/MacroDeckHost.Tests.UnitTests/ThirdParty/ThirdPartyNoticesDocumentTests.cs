using MacroDeckHost.Application.ThirdParty;
using MacroDeckHost.Infrastructure.ThirdParty;

namespace MacroDeckHost.Tests.UnitTests.ThirdParty;

public class ThirdPartyNoticesDocumentTests
{
	private static readonly string SectionRule = new('=', 80);
	private static readonly string TextRule = new('-', 80);

	[Test]
	public void The_notices_shipped_with_the_host_parse_into_a_consistent_document()
	{
		var document = new ThirdPartyNoticesFile().Read();

		Assert.That(document, Is.Not.Null, "THIRD-PARTY-NOTICES must be copied next to the host");
		var textIds = document!.Texts.Select(text => text.Id).ToHashSet();
		var referenced = document.Components.SelectMany(component => component.TextIds).ToHashSet();
		Assert.Multiple(() =>
		{
			Assert.That(document.Components.Select(component => component.Ecosystem).Distinct(),
				Is.EquivalentTo(new[] { "nuget", "npm", "cargo", "asset" }));
			Assert.That(referenced, Is.SubsetOf(textIds));
			Assert.That(textIds, Is.SubsetOf(referenced));
			Assert.That(document.Components, Has.All.Matches<ThirdPartyComponent>(component =>
				component.Licenses.Count > 0 && component.TextIds.Count > 0));
			Assert.That(document.Texts, Has.All.Matches<ThirdPartyText>(text => text.Content.Length > 0));
		});
	}

	[Test]
	public void Components_and_texts_are_read_field_by_field()
	{
		var text = string.Join('\n',
			"THIRD-PARTY SOFTWARE NOTICES AND INFORMATION",
			"",
			SectionRule, "Rust crates (Macro Deck desktop application)", SectionRule,
			"",
			"dual",
			"  License: MIT",
			"  Declared: MIT OR Apache-2.0",
			"  URL: https://example.com/dual",
			"  Platforms: Windows",
			"  Note: Vendored: part one.",
			"  Texts: [1], [2]",
			"",
			SectionRule, "License and notice texts", SectionRule,
			"",
			"[1] dual",
			TextRule,
			"First text",
			"[2] looks like a header but has no rule",
			SectionRule,
			"",
			"[2] dual",
			TextRule,
			"Second text",
			"");

		var document = ThirdPartyNoticesDocument.Parse(text);

		Assert.Multiple(() =>
		{
			Assert.That(document.Components, Has.Count.EqualTo(1));
			var component = document.Components[0];
			Assert.That(component.Name, Is.EqualTo("dual"));
			Assert.That(component.Ecosystem, Is.EqualTo("cargo"));
			Assert.That(component.Licenses, Is.EqualTo(new[] { "MIT" }));
			Assert.That(component.Declared, Is.EqualTo(new[] { "MIT OR Apache-2.0" }));
			Assert.That(component.Url, Is.EqualTo("https://example.com/dual"));
			Assert.That(component.Platforms, Is.EqualTo(new[] { "Windows" }));
			Assert.That(component.Note, Is.EqualTo("Vendored: part one."));
			Assert.That(component.TextIds, Is.EqualTo(new[] { 1, 2 }));
			Assert.That(document.Texts[0].Content,
				Is.EqualTo($"First text\n[2] looks like a header but has no rule\n{SectionRule}"));
			Assert.That(document.Texts[1].Content, Is.EqualTo("Second text"));
		});
	}

	[Test]
	public void A_host_without_the_notices_file_reports_it_missing()
	{
		var file = new ThirdPartyNoticesFile(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

		Assert.Multiple(() =>
		{
			Assert.That(file.Read(), Is.Null);
			Assert.That(file.ReadText(), Is.Null);
		});
	}
}
