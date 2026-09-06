using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A21 - <see cref="FakeWidgetApi" /> agrees with the host on what counts as a change, rather than a
/// fake that reports a clear-only request as a no-op and fails a plugin the real host would serve.
/// </summary>
[TestFixture]
public class A21_WidgetFakeTests
{
	private static FakeWidgetApi SeededApi()
	{
		var api = new FakeWidgetApi();
		api.Seed(new WidgetTargetInfo
		{
			Id = "widget-1",
			Label = "Play",
			Location = "Default / Home",
			Type = "action-button"
		});

		return api;
	}

	[Test]
	public async Task A_clear_only_request_is_applied_and_recorded()
	{
		var api = SeededApi();
		var request = new WidgetAppearanceRequest
		{
			WidgetId = "widget-1",
			Patch = new WidgetAppearancePatch(),
			ClearProperties = [WidgetAppearanceProperty.BackgroundColor]
		};

		var applied = await api.ApplyAsync(request);

		Assert.Multiple(() =>
		{
			Assert.That(applied, Is.True);
			Assert.That(api.LastApplied["widget-1"], Is.SameAs(request));
		});
	}

	[Test]
	public async Task A_request_that_neither_patches_nor_clears_is_a_no_op()
	{
		var api = SeededApi();

		var applied = await api.ApplyAsync(new WidgetAppearanceRequest
		{
			WidgetId = "widget-1",
			Patch = new WidgetAppearancePatch()
		});

		Assert.Multiple(() =>
		{
			Assert.That(applied, Is.False);
			Assert.That(api.LastApplied, Does.Not.ContainKey("widget-1"));
		});
	}

	[Test]
	public async Task A_clear_only_request_for_an_unknown_widget_is_a_no_op()
	{
		var api = SeededApi();

		var applied = await api.ApplyAsync(new WidgetAppearanceRequest
		{
			WidgetId = "deleted-widget",
			Patch = new WidgetAppearancePatch(),
			ClearProperties = [WidgetAppearanceProperty.BackgroundColor]
		});

		Assert.Multiple(() =>
		{
			Assert.That(applied, Is.False);
			Assert.That(api.LastApplied, Is.Empty);
		});
	}
}
