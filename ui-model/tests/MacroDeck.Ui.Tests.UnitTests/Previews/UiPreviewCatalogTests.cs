using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Previews;

namespace MacroDeck.Ui.Tests.UnitTests.Previews;

/// <summary>
/// The discovery half of issue #804: a preview scenario is explicitly registered, has a human-readable
/// name, groups under the view it previews, and costs a running Macro Deck nothing until it is opened.
/// </summary>
[TestFixture]
public class UiPreviewCatalogTests
{
	private static readonly string[] _spotifyScenarios = ["Connected", "Default"];

	private static readonly string[] _weatherScenarios = ["Long text"];

	private static readonly string[] _mixedRegistered = ["Default"];

	private static readonly UiSurface _surface = new()
	{
		Kind = UiSurfaceKinds.DeveloperPreview, SessionMode = UiSessionModes.Exclusive
	};

	[Test]
	public void Two_scenarios_on_one_view_and_one_on_another_are_three_previews_in_two_groups()
	{
		var found = Scan(nameof(PreviewFixtures.SpotifyConfigViewPreviews),
			nameof(PreviewFixtures.WeatherDetailsViewPreviews));

		Assert.Multiple(() =>
		{
			Assert.That(found, Has.Count.EqualTo(3));
			Assert.That(ScenariosOf(found, "SpotifyConfigView"), Is.EqualTo(_spotifyScenarios));
			Assert.That(ScenariosOf(found, "WeatherDetailsView"), Is.EqualTo(_weatherScenarios));
			Assert.That(found.Select(preview => preview.Declaration.Id).Distinct().Count(), Is.EqualTo(3));
		});
	}

	[Test]
	public void Discovering_a_scenario_does_not_run_it()
	{
		PreviewFixtures.SpotifyConfigViewPreviews.ResetInvocations();

		Scan(nameof(PreviewFixtures.SpotifyConfigViewPreviews));

		Assert.That(PreviewFixtures.SpotifyConfigViewPreviews.Invocations, Is.Zero);
	}

	[Test]
	public void A_scenario_is_built_only_when_it_is_opened_and_is_rebuilt_for_every_opening()
	{
		PreviewFixtures.SpotifyConfigViewPreviews.ResetInvocations();
		var preview = Single(nameof(PreviewFixtures.SpotifyConfigViewPreviews), "Connected");

		var first = preview.Create(_surface);
		var second = preview.Create(_surface);

		Assert.Multiple(() =>
		{
			Assert.That(PreviewFixtures.SpotifyConfigViewPreviews.Invocations, Is.EqualTo(2));
			Assert.That(second.View, Is.Not.SameAs(first.View));
			Assert.That(first.View.Surface.Kind, Is.EqualTo(UiSurfaceKinds.DeveloperPreview));
			Assert.That(UiCanonicalJson.Serialize(first.View.Tree), Does.Contain("Connected"));
		});
	}

	[Test]
	public void A_view_name_defaults_to_the_declaring_type_without_its_previews_suffix_and_can_be_overridden()
	{
		var derived = Single(nameof(PreviewFixtures.SpotifyConfigViewPreviews), "Default");
		var explicitly = Single(nameof(PreviewFixtures.ExplicitlyNamed), "Default");

		Assert.Multiple(() =>
		{
			Assert.That(derived.Declaration.View, Is.EqualTo("SpotifyConfigView"));
			Assert.That(explicitly.Declaration.View, Is.EqualTo("SpotifyConfigView"));
			Assert.That(explicitly.Declaration.Id, Is.Not.EqualTo(derived.Declaration.Id));
		});
	}

	[Test]
	public void A_malformed_scenario_is_reported_and_the_well_formed_ones_beside_it_still_register()
	{
		var result = UiPreviewCatalog.Scan(typeof(PreviewFixtures).Assembly);
		var mixed = nameof(PreviewFixtures.MixedPreviews);

		var registered = result.Registrations
			.Where(preview => DeclaredIn(preview.Declaration.Id, mixed))
			.Select(preview => preview.Declaration.Scenario)
			.ToList();
		var skipped = result.Diagnostics
			.Where(diagnostic => diagnostic.Member.Contains(mixed, StringComparison.Ordinal))
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(registered, Is.EqualTo(_mixedRegistered));
			Assert.That(skipped.Select(diagnostic => diagnostic.Member),
				Is.EquivalentTo(new[]
				{
					$"{typeof(PreviewFixtures.MixedPreviews).FullName}.Instance",
					$"{typeof(PreviewFixtures.MixedPreviews).FullName}.Parameterized",
					$"{typeof(PreviewFixtures.MixedPreviews).FullName}.WrongReturn"
				}));
			Assert.That(skipped.Select(diagnostic => diagnostic.Reason), Has.All.Not.Empty);
		});
	}

	[Test]
	public async Task Disposing_a_rendering_releases_what_the_scenario_handed_over()
	{
		PreviewFixtures.OwnedResourcePreviews.Created.Clear();
		var preview = Single(nameof(PreviewFixtures.OwnedResourcePreviews), "Owns a mock");

		var instance = preview.Create(_surface);
		var mock = PreviewFixtures.OwnedResourcePreviews.Created.Single();

		Assert.That(mock.Disposed, Is.False);

		await instance.DisposeAsync();

		Assert.That(mock.Disposed, Is.True);
	}

	private static List<UiPreviewRegistration> Scan(params string[] fixtures)
		=> UiPreviewCatalog.Scan(typeof(PreviewFixtures).Assembly)
			.Registrations
			.Where(preview => fixtures.Any(fixture => DeclaredIn(preview.Declaration.Id, fixture)))
			.ToList();

	private static UiPreviewRegistration Single(string fixture, string scenario)
		=> Scan(fixture).Single(preview =>
			string.Equals(preview.Declaration.Scenario, scenario, StringComparison.Ordinal));

	private static bool DeclaredIn(string id, string fixture)
		=> id.Contains($"+{fixture}.", StringComparison.Ordinal);

	private static List<string> ScenariosOf(IEnumerable<UiPreviewRegistration> found, string view)
		=> found.Where(preview => string.Equals(preview.Declaration.View, view, StringComparison.Ordinal))
			.Select(preview => preview.Declaration.Scenario)
			.Order(StringComparer.Ordinal)
			.ToList();
}
