using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.OptionsSources;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
internal sealed class IconAppearanceVariantTests
{
	private static readonly IconEntity _parent = new()
	{
		Id = Guid.NewGuid(),
		PackId = Guid.Empty,
		Name = "parent",
		ProcessingState = IconProcessingState.Ready
	};

	[TestCase("outlined", "Outlined")]
	[TestCase("duoTone", "Duo tone")]
	[TestCase("red", "Red")]
	[TestCase("style2", "Style2")]
	[TestCase("iOS", "I OS")]
	public void A_variant_token_reads_as_a_sentence_case_name(string token, string expected)
		=> Assert.That(IconAppearanceTraits.Humanize(token), Is.EqualTo(expected));

	[TestCase("variant=duoTone", true, "Duo tone")]
	[TestCase("variant=Duo", false, "")]
	[TestCase("colorScheme=dark", false, "")]
	[TestCase("colorScheme=dark;variant=red", false, "")]
	[TestCase("variant=red;variant=blue", false, "")]
	public void Only_a_lone_variant_trait_has_a_variant_name(string key, bool isVariant, string expected)
	{
		Assert.Multiple(() =>
		{
			Assert.That(IconAppearanceTraits.TryGetVariantName(key, out var name), Is.EqualTo(isVariant));
			Assert.That(name, Is.EqualTo(expected));
		});
	}

	[TestCase(null, null)]
	[TestCase("dark", "static")]
	[TestCase("light", "animated")]
	public void A_variant_appearance_is_never_picked_automatically(string? scheme, string? motion)
	{
		var variants = new[] { Asset("variant=outlined"), Asset("colorScheme=dark;variant=red") };

		var selected = IconAppearanceSelector.Select(_parent, variants, new IconAppearanceContext(scheme, motion));

		Assert.That(selected, Is.SameAs(_parent));
	}

	[Test]
	public void A_built_in_appearance_still_wins_next_to_variants()
	{
		var dark = Asset("colorScheme=dark");

		var selected = IconAppearanceSelector.Select(_parent,
			[Asset("variant=outlined"), dark],
			new IconAppearanceContext("dark", "animated"));

		Assert.That(selected, Is.SameAs(dark));
	}

	[Test]
	public void A_variant_can_be_pinned_by_its_key()
	{
		var outlined = Asset("variant=outlined");

		Assert.Multiple(() =>
		{
			Assert.That(IconAppearanceSelector.FindPinned([Asset("variant=red"), outlined], "variant=outlined"),
				Is.SameAs(outlined));
			Assert.That(IconAppearanceSelector.FindPinned([outlined], "variant=gone"), Is.Null);
		});
	}

	[Test]
	public async Task The_options_source_offers_the_built_ins_then_each_variant_present_once()
	{
		using var harness = new IconTestHarness();
		var pack = await harness.CreatePack();
		var lamp = await harness.AddReadyIcon(pack.Id, "lamp", [1]);
		var door = await harness.AddReadyIcon(pack.Id, "door", [2]);
		await harness.AddReadyAppearance(lamp, "variant=outlined", [3]);
		await harness.AddReadyAppearance(lamp, "colorScheme=dark", [4]);
		await harness.AddReadyAppearance(door, "variant=outlined", [5]);
		await harness.AddReadyAppearance(door, "variant=duoTone", [6]);

		var result = await new IconAppearancesOptionsSource(harness.Cache)
			.GetOptionsAsync(null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.AllowsCustomValue, Is.True);
			Assert.That(result.Options.Select(option => option.Value), Is.EqualTo(new[]
			{
				WidgetAppearanceValues.Reset, "default", "colorScheme=light", "colorScheme=dark", "motion=static", "motion=animated",
				"colorScheme=light;motion=static", "colorScheme=light;motion=animated",
				"colorScheme=dark;motion=static", "colorScheme=dark;motion=animated",
				"variant=duoTone", "variant=outlined"
			}));
		});
	}

	private static IconEntity Asset(string key)
	{
		Assert.That(IconAppearanceTraits.TryParse(key, out var traits), Is.True);
		return new IconEntity
		{
			Id = Guid.NewGuid(),
			PackId = Guid.Empty,
			Name = key,
			ProcessingState = IconProcessingState.Ready,
			AppearanceOfId = _parent.Id,
			AppearanceTraits = traits
		};
	}
}
