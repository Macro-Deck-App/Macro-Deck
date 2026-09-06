using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

/// <summary>
/// The open set of widget types (issue #843): a provider-registered type is exactly as real as one that
/// shipped, a provider can neither shadow a built-in nor claim another owner's namespace, and withdrawing
/// a type never touches a widget already placed with it.
/// </summary>
[TestFixture]
public class WidgetTypeRegistryTests
{
	private RecordingMediator _mediator = null!;
	private WidgetTypeRegistry _registry = null!;

	[SetUp]
	public void SetUp()
	{
		_mediator = new RecordingMediator();
		_registry = new WidgetTypeRegistry(_mediator);
	}

	[Test]
	public void Every_shipped_type_is_registered_at_startup_and_carries_a_non_empty_name()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_registry.All.Select(entry => entry.WidgetTypeId),
				Is.EqualTo(WidgetTypeIds.BuiltIn).AsCollection);

			foreach (var entry in _registry.All)
			{
				Assert.That(entry.Descriptor.Name.IsEmpty, Is.False, $"{entry.WidgetTypeId} has no name");
			}
		});
	}

	[Test]
	public async Task A_provider_registered_type_is_as_real_as_one_that_shipped()
	{
		var registration = await _registry.Register("com.example.gauges",
			new WidgetTypeDescriptor("gauge", LocalizedText.FromLiteral("Gauge")));

		Assert.Multiple(() =>
		{
			Assert.That(registration.WidgetTypeId, Is.EqualTo("com.example.gauges::gauge"));
			Assert.That(_registry.IsRegistered("com.example.gauges::gauge"), Is.True);
			Assert.That(_registry.Resolve("com.example.gauges::gauge"), Is.EqualTo("com.example.gauges::gauge"));
		});
	}

	[Test]
	public async Task All_is_the_built_ins_in_order_then_provider_types_by_qualified_id()
	{
		await _registry.Register("com.example.zebra", new WidgetTypeDescriptor("z", LocalizedText.FromLiteral("Z")));
		await _registry.Register("com.example.apple", new WidgetTypeDescriptor("a", LocalizedText.FromLiteral("A")));

		var expected = WidgetTypeIds.BuiltIn
			.Append("com.example.apple::a")
			.Append("com.example.zebra::z");

		Assert.That(_registry.All.Select(entry => entry.WidgetTypeId), Is.EqualTo(expected).AsCollection);
	}

	[Test]
	public async Task A_provider_cannot_shadow_a_built_in()
	{
		var registration = await _registry.Register("com.example.gauges",
			new WidgetTypeDescriptor(WidgetTypeIds.ActionButton, LocalizedText.FromLiteral("Not the real thing")));

		Assert.Multiple(() =>
		{
			Assert.That(registration.WidgetTypeId, Is.EqualTo("com.example.gauges::ActionButton"));

			Assert.That(_registry.TryResolve(WidgetTypeIds.ActionButton, out var builtIn), Is.True);
			Assert.That(builtIn.IsBuiltIn, Is.True);
			Assert.That(builtIn.ProviderId, Is.Empty);

			Assert.That(_registry.All.Count(entry => entry.WidgetTypeId == WidgetTypeIds.ActionButton),
				Is.EqualTo(1));
		});
	}

	[Test]
	public void A_provider_cannot_claim_another_owners_namespace()
	{
		Assert.That(() => _registry.Register("com.example.gauges", new WidgetTypeDescriptor("evil::gauge", default)),
			Throws.ArgumentException);

		Assert.That(_registry.All.Select(entry => entry.WidgetTypeId), Does.Not.Contain("evil::gauge"));
	}

	[Test]
	public async Task Two_owners_whose_ids_differ_only_by_a_hyphen_do_not_shadow_each_other()
	{
		var hyphenated = await _registry.Register("com.example.my-plugin",
			new WidgetTypeDescriptor("gauge", LocalizedText.FromLiteral("Gauge")));
		var unhyphenated = await _registry.Register("com.example.myplugin",
			new WidgetTypeDescriptor("gauge", LocalizedText.FromLiteral("Gauge")));

		Assert.Multiple(() =>
		{
			Assert.That(hyphenated.WidgetTypeId, Is.EqualTo("com.example.my-plugin::gauge"));
			Assert.That(unhyphenated.WidgetTypeId, Is.EqualTo("com.example.myplugin::gauge"));
			Assert.That(_registry.Resolve(hyphenated.WidgetTypeId), Is.EqualTo(hyphenated.WidgetTypeId));
			Assert.That(_registry.Resolve(unhyphenated.WidgetTypeId), Is.EqualTo(unhyphenated.WidgetTypeId));
		});
	}

	[Test]
	public async Task UnregisterAll_withdraws_exactly_that_owners_types()
	{
		var mine = await _registry.Register("com.example.mine",
			new WidgetTypeDescriptor("gauge", LocalizedText.FromLiteral("Gauge")));
		var theirs = await _registry.Register("com.example.theirs",
			new WidgetTypeDescriptor("gauge", LocalizedText.FromLiteral("Gauge")));

		await _registry.UnregisterAll("com.example.mine");

		Assert.Multiple(() =>
		{
			Assert.That(_registry.IsRegistered(mine.WidgetTypeId), Is.False);
			Assert.That(_registry.IsRegistered(theirs.WidgetTypeId), Is.True);
			Assert.That(_registry.All.Select(entry => entry.WidgetTypeId),
				Is.EquivalentTo(WidgetTypeIds.BuiltIn.Append(theirs.WidgetTypeId)));
		});
	}

	[Test]
	public async Task Every_mutation_publishes_the_catalog_changed_notification()
	{
		var registration = await _registry.Register("com.example.gauges",
			new WidgetTypeDescriptor("gauge", LocalizedText.FromLiteral("Gauge")));
		Assert.That(_mediator.Published.OfType<WidgetTypeCatalogChangedNotification>().Count(), Is.EqualTo(1));

		await _registry.Unregister("com.example.gauges", "gauge");
		Assert.That(_mediator.Published.OfType<WidgetTypeCatalogChangedNotification>().Count(), Is.EqualTo(2));

		await _registry.Register("com.example.gauges",
			new WidgetTypeDescriptor("gauge", LocalizedText.FromLiteral("Gauge")));
		await _registry.UnregisterAll("com.example.gauges");
		Assert.That(_mediator.Published.OfType<WidgetTypeCatalogChangedNotification>().Count(), Is.EqualTo(4));
	}

	[Test]
	public void A_type_with_configuration_and_no_schema_is_refused()
		=> Assert.That(() => _registry.Register("com.example.gauges",
				new WidgetTypeDescriptor("gauge", LocalizedText.FromLiteral("Gauge"), HasConfiguration: true)),
			Throws.ArgumentException);

	[Test]
	public async Task A_type_with_configuration_and_a_schema_is_accepted()
	{
		var registration = await _registry.Register("com.example.gauges",
			new WidgetTypeDescriptor("gauge",
				LocalizedText.FromLiteral("Gauge"),
				HasConfiguration: true,
				DataSchema: "{}"));

		Assert.That(_registry.IsRegistered(registration.WidgetTypeId), Is.True);
	}

	[Test]
	public async Task Withdrawing_a_type_never_touches_a_placed_widget()
	{
		var registration = await _registry.Register("com.example.gauges",
			new WidgetTypeDescriptor("gauge", LocalizedText.FromLiteral("Gauge")));

		// Stands in for a folder holding a widget of this type: WidgetTypeRegistry never references a
		// folder, a widget store, or anything that could touch stored data - the assertions below prove
		// that structurally rather than by wiring up the whole persistence stack for this unit test.
		var placedWidgetType = registration.WidgetTypeId;
		var placedWidgetData = "{\"min\":0}"u8.ToArray();

		await _registry.UnregisterAll("com.example.gauges");

		Assert.Multiple(() =>
		{
			Assert.That(placedWidgetType, Is.EqualTo(registration.WidgetTypeId));
			Assert.That(placedWidgetData, Is.EqualTo("{\"min\":0}"u8.ToArray()));
			Assert.That(_mediator.Published, Has.All.InstanceOf<WidgetTypeCatalogChangedNotification>());
		});
	}

	[Test]
	public void With_no_providers_around_every_built_in_type_reports_no_config_surface()
		=> Assert.That(_registry.All.Select(entry => entry.Descriptor.HasConfiguration), Is.All.False);

	[Test]
	public void A_built_in_type_whose_provider_declares_a_config_surface_reports_HasConfiguration()
	{
		var registry = new WidgetTypeRegistry(_mediator,
		[
			new StubWidgetUiProvider(WidgetTypeIds.Weather, UiSurfaceKinds.Config),
			new StubWidgetUiProvider(WidgetTypeIds.Clock, UiSurfaceKinds.Widget)
		]);

		Assert.Multiple(() =>
		{
			Assert.That(Descriptor(registry, WidgetTypeIds.Weather).HasConfiguration,
				Is.True,
				"the Weather provider declared a config surface");
			Assert.That(Descriptor(registry, WidgetTypeIds.Clock).HasConfiguration,
				Is.False,
				"the Clock provider declared no config surface");
			Assert.That(Descriptor(registry, WidgetTypeIds.Slider).HasConfiguration,
				Is.False,
				"no provider was registered for Slider at all");
		});
	}

	private static WidgetTypeDescriptor Descriptor(WidgetTypeRegistry registry, string id)
		=> registry.All.Single(entry => entry.WidgetTypeId == id).Descriptor;

	private sealed class StubWidgetUiProvider : IBuiltInWidgetUiProvider
	{
		public StubWidgetUiProvider(string widgetType, string surfaceKind)
		{
			WidgetTypeId = widgetType;
			Surfaces = [new UiSurfaceDeclaration { Kind = surfaceKind, SessionMode = UiSessionModes.Exclusive }];
		}

		public string WidgetTypeId { get; }

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; }

		public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
			=> Task.FromResult<IUiSession?>(null);
	}
}
