using System.Text.Json;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

[TestFixture]
public class A36WidgetDataAndHistoryFakeTests
{
	[Test]
	public async Task Only_a_seeded_widget_has_data()
	{
		var api = new FakeWidgetApi();
		using var document = JsonDocument.Parse("""{"allowModeration":false}""");
		api.SeedData(new WidgetDataSnapshot("widget-1", "com.example::chat", document.RootElement.Clone()));

		var seeded = await api.GetWidgetDataAsync("widget-1");
		var other = await api.GetWidgetDataAsync("widget-2");

		Assert.Multiple(() =>
		{
			Assert.That(seeded?.Data.GetProperty("allowModeration").GetBoolean(), Is.False);
			Assert.That(other, Is.Null);
		});
	}

	[Test]
	public async Task A_history_window_follows_the_recorded_samples_up_to_its_capacity()
	{
		var api = new FakeVariableApi();
		await using var window = await api.OpenHistoryAsync("viewer_count", 2);
		var changes = 0;
		window.Changed += (_, _) => changes++;

		api.RecordSample("viewer_count", 1);
		api.RecordSample("viewer_count", 2);
		api.RecordSample("viewer_count", 3);
		api.RecordSample("follower_count", 9);

		Assert.Multiple(() =>
		{
			Assert.That(window.Values, Is.EqualTo(new double?[] { 2, 3 }));
			Assert.That(changes, Is.EqualTo(3));
			Assert.That(api.OpenHistoryWindows, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Disposing_a_window_releases_it()
	{
		var api = new FakeVariableApi();
		var window = await api.OpenHistoryAsync("viewer_count", 10);

		await window.DisposeAsync();

		Assert.That(api.OpenHistoryWindows, Is.Zero);
	}
}
