using MacroDeck.Localization;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.FolderViews;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A25 - <see cref="FakeFolderViewProviderContext" /> enforces the same identity and validation rules the
/// host does, so a plugin author's green test against it is not lying about what a real host would do.
/// </summary>
[TestFixture]
public class A25_FolderViewProviderFakeTests
{
	private static FolderViewDescriptor View(string id, bool hasConfiguration = false)
		=> new(id, LocalizedText.FromLiteral("Dashboard"), HasConfiguration: hasConfiguration);

	[Test]
	public async Task Registering_the_same_local_id_again_replaces_rather_than_duplicates()
	{
		var context = new FakeFolderViewProviderContext();

		await context.RegisterFolderViewAsync(View("dashboard"));
		await context.RegisterFolderViewAsync(View("dashboard", hasConfiguration: true));

		Assert.Multiple(() =>
		{
			Assert.That(context.FolderViews, Has.Count.EqualTo(1));
			Assert.That(context.FolderViews["dashboard"].HasConfiguration, Is.True);
		});
	}

	[Test]
	public async Task Unregistering_an_unknown_id_is_a_silent_no_op()
	{
		var context = new FakeFolderViewProviderContext();
		await context.RegisterFolderViewAsync(View("dashboard"));

		Assert.DoesNotThrowAsync(() => context.UnregisterFolderViewAsync("never-registered"));
		Assert.That(context.FolderViews, Has.Count.EqualTo(1));
	}

	[Test]
	public void An_empty_id_is_rejected_the_way_the_host_rejects_it()
	{
		var context = new FakeFolderViewProviderContext();

		Assert.That(() => context.RegisterFolderViewAsync(View(string.Empty)), Throws.ArgumentException);
	}

	[Test]
	public void An_empty_name_is_rejected_the_way_the_host_rejects_it()
	{
		var context = new FakeFolderViewProviderContext();

		Assert.That(() => context.RegisterFolderViewAsync(new FolderViewDescriptor("dashboard", default)),
			Throws.ArgumentException);
	}

	/// <summary>An unrecognised navigation mode reads as the default, so a mode a later Macro Deck names
	/// never leaves a user without a way out of the view.</summary>
	[TestCase(null)]
	[TestCase("default")]
	[TestCase("something-new")]
	public void An_unrecognised_navigation_mode_resolves_to_the_default(string? navigation)
		=> Assert.That(new FolderViewDescriptor("dashboard", LocalizedText.FromLiteral("D"), Navigation: navigation)
				.ResolvedNavigation,
			Is.EqualTo(FolderViewNavigation.Default));

	[Test]
	public void Hidden_is_the_one_mode_that_suppresses_the_default()
		=> Assert.That(new FolderViewDescriptor("dashboard", LocalizedText.FromLiteral("D"), Navigation: "hidden")
				.ResolvedNavigation,
			Is.EqualTo(FolderViewNavigation.Hidden));

	[Test]
	public async Task Every_call_is_recorded_in_order()
	{
		var context = new FakeFolderViewProviderContext();

		await context.RegisterFolderViewAsync(View("dashboard"));
		await context.UnregisterFolderViewAsync("dashboard");

		Assert.That(context.Calls.Select(call => (call.Kind, call.FolderViewId)),
			Is.EqualTo(new[]
			{
				(FolderViewProviderCallKind.Register, "dashboard"),
				(FolderViewProviderCallKind.Unregister, "dashboard")
			}));
	}
}
