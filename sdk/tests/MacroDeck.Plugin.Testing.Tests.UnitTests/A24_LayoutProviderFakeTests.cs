using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Layouts;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A24 - <see cref="FakeLayoutProviderContext" /> enforces the same identity and validation rules the
/// host does, so a plugin author's green test against it is not lying about what a real host would do.
/// </summary>
[TestFixture]
public class A24_LayoutProviderFakeTests
{
	private static LayoutDescriptor Layout(string id, int rows, int columns)
		=> new(id,
			"Test Layout",
			[
				new LayoutRegion
				{
					Id = "grid", Kind = LayoutRegionKinds.Grid, Grid = new LayoutGrid { Rows = rows, Columns = columns }
				}
			]);

	[Test]
	public async Task Registering_the_same_local_id_again_replaces_rather_than_duplicates()
	{
		var context = new FakeLayoutProviderContext();

		await context.RegisterLayoutAsync(Layout("xl", 4, 8));
		await context.RegisterLayoutAsync(Layout("xl", 2, 4));

		Assert.Multiple(() =>
		{
			Assert.That(context.Layouts, Has.Count.EqualTo(1));
			Assert.That(context.Layouts["xl"].PrimaryGrid!.Grid!.Rows, Is.EqualTo(2));
			Assert.That(context.Layouts["xl"].PrimaryGrid!.Grid!.Columns, Is.EqualTo(4));
		});
	}

	[Test]
	public async Task Unregistering_an_unknown_id_is_a_silent_no_op()
	{
		var context = new FakeLayoutProviderContext();
		await context.RegisterLayoutAsync(Layout("xl", 4, 8));

		Assert.DoesNotThrowAsync(() => context.UnregisterLayoutAsync("never-registered"));

		Assert.That(context.Layouts, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task Unregistering_a_known_id_removes_it()
	{
		var context = new FakeLayoutProviderContext();
		await context.RegisterLayoutAsync(Layout("xl", 4, 8));

		await context.UnregisterLayoutAsync("xl");

		Assert.That(context.Layouts, Is.Empty);
	}

	[Test]
	public void An_empty_id_throws()
	{
		var context = new FakeLayoutProviderContext();

		Assert.ThrowsAsync<ArgumentException>(() => context.RegisterLayoutAsync(Layout("", 4, 8)));
	}

	[Test]
	public void An_empty_name_throws()
	{
		var context = new FakeLayoutProviderContext();
		var layout = new LayoutDescriptor("xl", "", []);

		Assert.ThrowsAsync<ArgumentException>(() => context.RegisterLayoutAsync(layout));
	}

	[Test]
	public void A_duplicated_region_id_within_one_layout_throws()
	{
		var context = new FakeLayoutProviderContext();
		var layout = new LayoutDescriptor("xl",
			"Test Layout",
			[
				new LayoutRegion { Id = "region", Kind = LayoutRegionKinds.Encoder, Count = 2 },
				new LayoutRegion { Id = "region", Kind = LayoutRegionKinds.Pedal, Count = 1 }
			]);

		Assert.ThrowsAsync<ArgumentException>(() => context.RegisterLayoutAsync(layout));
	}

	[Test]
	public async Task Calls_are_recorded_in_order()
	{
		var context = new FakeLayoutProviderContext();

		await context.RegisterLayoutAsync(Layout("xl", 4, 8));
		await context.UnregisterLayoutAsync("xl");

		Assert.Multiple(() =>
		{
			Assert.That(context.Calls, Has.Count.EqualTo(2));
			Assert.That(context.Calls[0].Kind, Is.EqualTo(LayoutProviderCallKind.Register));
			Assert.That(context.Calls[1].Kind, Is.EqualTo(LayoutProviderCallKind.Unregister));
		});
	}
}
