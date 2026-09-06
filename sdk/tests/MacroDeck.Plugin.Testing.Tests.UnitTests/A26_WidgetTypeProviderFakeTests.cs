using MacroDeck.Localization;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A26 - <see cref="FakeWidgetTypeProviderContext" /> enforces the same identity and validation rules the
/// host does, so a plugin author's green test against it is not lying about what a real host would do.
/// </summary>
[TestFixture]
public class A26_WidgetTypeProviderFakeTests
{
	private static WidgetTypeDescriptor Type(
		string id,
		bool hasConfiguration = false,
		string? defaultData = null,
		string? dataSchema = null)
		=> new(id,
			LocalizedText.FromLiteral("Gauge"),
			DefaultData: defaultData,
			DataSchema: dataSchema,
			HasConfiguration: hasConfiguration);

	[Test]
	public async Task Registering_the_same_local_id_again_replaces_rather_than_duplicates()
	{
		var context = new FakeWidgetTypeProviderContext();

		await context.RegisterWidgetTypeAsync(Type("gauge"));
		await context.RegisterWidgetTypeAsync(Type("gauge",
			hasConfiguration: true,
			dataSchema: "{\"type\":\"object\"}"));

		Assert.Multiple(() =>
		{
			Assert.That(context.WidgetTypes, Has.Count.EqualTo(1));
			Assert.That(context.WidgetTypes["gauge"].HasConfiguration, Is.True);
		});
	}

	[Test]
	public async Task Unregistering_an_unknown_id_is_a_silent_no_op()
	{
		var context = new FakeWidgetTypeProviderContext();
		await context.RegisterWidgetTypeAsync(Type("gauge"));

		Assert.DoesNotThrowAsync(() => context.UnregisterWidgetTypeAsync("never-registered"));
		Assert.That(context.WidgetTypes, Has.Count.EqualTo(1));
	}

	[Test]
	public void An_empty_id_is_rejected_the_way_the_host_rejects_it()
	{
		var context = new FakeWidgetTypeProviderContext();

		Assert.That(() => context.RegisterWidgetTypeAsync(Type(string.Empty)), Throws.ArgumentException);
	}

	[Test]
	public void An_empty_name_is_rejected_the_way_the_host_rejects_it()
	{
		var context = new FakeWidgetTypeProviderContext();

		Assert.That(() => context.RegisterWidgetTypeAsync(new WidgetTypeDescriptor("gauge", default)),
			Throws.ArgumentException);
	}

	[Test]
	public void Default_data_that_is_not_a_JSON_object_is_rejected_the_way_the_host_rejects_it()
	{
		var context = new FakeWidgetTypeProviderContext();

		Assert.That(() => context.RegisterWidgetTypeAsync(Type("gauge", defaultData: "[1, 2, 3]")),
			Throws.ArgumentException);
	}

	[Test]
	public void A_data_schema_that_does_not_parse_as_JSON_Schema_is_rejected_the_way_the_host_rejects_it()
	{
		var context = new FakeWidgetTypeProviderContext();

		Assert.That(() => context.RegisterWidgetTypeAsync(Type("gauge", dataSchema: "not json at all")),
			Throws.ArgumentException);
	}

	[Test]
	public void Declaring_configuration_with_no_data_schema_is_rejected_the_way_the_host_rejects_it()
	{
		var context = new FakeWidgetTypeProviderContext();

		Assert.That(() => context.RegisterWidgetTypeAsync(Type("gauge", hasConfiguration: true)),
			Throws.ArgumentException);
	}

	[Test]
	public async Task Every_call_is_recorded_in_order()
	{
		var context = new FakeWidgetTypeProviderContext();

		await context.RegisterWidgetTypeAsync(Type("gauge"));
		await context.UnregisterWidgetTypeAsync("gauge");

		Assert.That(context.Calls.Select(call => (call.Kind, call.WidgetTypeId)),
			Is.EqualTo(new[]
			{
				(WidgetTypeProviderCallKind.Register, "gauge"),
				(WidgetTypeProviderCallKind.Unregister, "gauge")
			}));
	}
}
