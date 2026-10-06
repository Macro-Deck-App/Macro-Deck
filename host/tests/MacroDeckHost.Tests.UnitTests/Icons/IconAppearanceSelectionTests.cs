using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
internal sealed class IconAppearanceSelectionTests
{
	private static readonly IconEntity _parent = Icon("parent");

	[TestCase("motion=static;colorScheme=dark", "colorScheme=dark;motion=static")]
	[TestCase("colorScheme=light", "colorScheme=light")]
	[TestCase("motion=animated;season=winter", "motion=animated;season=winter")]
	public void A_key_reads_back_in_its_canonical_order(string key, string canonical)
	{
		Assert.Multiple(() =>
		{
			Assert.That(IconAppearanceTraits.TryParse(key, out var traits), Is.True);
			Assert.That(IconAppearanceTraits.ToKey(traits), Is.EqualTo(canonical));
		});
	}

	[TestCase("")]
	[TestCase("colorScheme")]
	[TestCase("ColorScheme=dark")]
	[TestCase("colorScheme=dark;colorScheme=light")]
	[TestCase("colorScheme=Dark Mode")]
	[TestCase("a=x;b=x;c=x;d=x;e=x")]
	public void A_malformed_key_is_rejected(string key)
		=> Assert.That(IconAppearanceTraits.TryParse(key, out _), Is.False);

	[Test]
	public void An_icon_without_appearances_is_its_own_image_in_every_context()
		=> Assert.That(IconAppearanceSelector.Select(_parent, [], new IconAppearanceContext("dark", "static")),
			Is.SameAs(_parent));

	[TestCase("dark", "animated", "colorScheme=dark")]
	[TestCase("light", "animated", null)]
	[TestCase(null, null, null)]
	public void A_colour_scheme_appearance_is_used_only_for_its_own_scheme(string? scheme,
		string? motion,
		string? expected)
		=> AssertSelects([Asset("colorScheme=dark")], new IconAppearanceContext(scheme, motion), expected);

	[TestCase("animated", "motion=animated")]
	[TestCase("static", null)]
	public void A_static_default_with_an_animated_appearance_animates_only_where_motion_is_allowed(string motion,
		string? expected)
		=> AssertSelects([Asset("motion=animated")], new IconAppearanceContext("light", motion), expected);

	[TestCase("static", "motion=static")]
	[TestCase("animated", null)]
	public void An_animated_default_with_a_static_appearance_stays_still_under_reduced_motion(string motion,
		string? expected)
		=> AssertSelects([Asset("motion=static")], new IconAppearanceContext("dark", motion), expected);

	[Test]
	public void The_appearance_matching_most_of_the_context_wins()
		=> AssertSelects([Asset("colorScheme=dark"), Asset("motion=static"), Asset("colorScheme=dark;motion=static")],
			new IconAppearanceContext("dark", "static"),
			"colorScheme=dark;motion=static");

	[TestCase("static", "motion=static")]
	[TestCase("animated", "colorScheme=dark")]
	public void A_tie_prefers_stillness_under_reduced_motion_and_the_colour_scheme_otherwise(string motion,
		string expected)
		=> AssertSelects([Asset("colorScheme=dark"), Asset("motion=" + motion)],
			new IconAppearanceContext("dark", motion),
			expected);

	[Test]
	public void An_appearance_that_is_not_ready_or_names_an_unknown_trait_is_never_picked()
	{
		var pending = Asset("colorScheme=dark");
		pending.ProcessingState = IconProcessingState.Pending;

		AssertSelects([pending, Asset("colorScheme=dark;season=winter")],
			new IconAppearanceContext("dark", "animated"),
			null);
	}

	private static void AssertSelects(IReadOnlyList<IconEntity> appearances,
		IconAppearanceContext context,
		string? expectedKey)
	{
		var selected = IconAppearanceSelector.Select(_parent, appearances, context);
		var selectedKey = selected.AppearanceTraits is { } traits ? IconAppearanceTraits.ToKey(traits) : null;

		Assert.That(selectedKey, Is.EqualTo(expectedKey));
	}

	private static IconEntity Asset(string key)
	{
		Assert.That(IconAppearanceTraits.TryParse(key, out var traits), Is.True);
		var asset = Icon(key);
		asset.AppearanceOfId = _parent.Id;
		asset.AppearanceTraits = traits;
		return asset;
	}

	private static IconEntity Icon(string name)
		=> new()
		{
			Id = Guid.NewGuid(),
			PackId = Guid.Empty,
			Name = name,
			ProcessingState = IconProcessingState.Ready
		};
}
