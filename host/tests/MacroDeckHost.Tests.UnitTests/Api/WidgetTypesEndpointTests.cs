using MacroDeck.Localization;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Model.Versioning;
using MacroDeckHost.Api.Controllers;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Api;

/// <summary>
/// <c>GET /api/widgets/types</c> (issue #837): what a client needs to decide whether it can offer a
/// widget's configuration as a Macro Deck UI tree, or has to fall back to its own editor.
/// </summary>
[TestFixture]
public class WidgetTypesEndpointTests
{
	private static WidgetsController CreateController(
		IUiTransportMessageHandler<GetWidgetTypesRequest, GetWidgetTypesResponse> handler)
		=> new(null!, null!, null!, null!, null!, null!, null!, null!, null!, handler, null!, null!, null!, null!);

	[Test]
	public async Task GetTypes_returns_one_entry_per_registered_type_carrying_its_id_and_isBuiltIn()
	{
		var registry = new FakeWidgetTypeRegistry(new WidgetTypeCatalogEntry(WidgetTypeIds.Weather,
				string.Empty,
				new WidgetTypeDescriptor(WidgetTypeIds.Weather, LocalizedText.FromLiteral("Weather"))),
			new WidgetTypeCatalogEntry("com.example::gauge",
				"com.example",
				new WidgetTypeDescriptor("gauge", LocalizedText.FromLiteral("Gauge"))));
		var controller = CreateController(new GetWidgetTypesRequestMessageHandler(registry));

		var response = await controller.GetTypes(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(response.Types, Has.Count.EqualTo(2));

			var weather = response.Types.Single(type => type.Id == WidgetTypeIds.Weather);
			Assert.That(weather.IsBuiltIn, Is.True);

			var gauge = response.Types.Single(type => type.Id == "com.example::gauge");
			Assert.That(gauge.IsBuiltIn, Is.False);
		});
	}

	[Test]
	public async Task GetTypes_reports_supportsConfigUi_and_the_current_model_version_only_where_it_is_set()
	{
		var registry = new FakeWidgetTypeRegistry(new WidgetTypeCatalogEntry(WidgetTypeIds.Weather,
				string.Empty,
				new WidgetTypeDescriptor(WidgetTypeIds.Weather,
					LocalizedText.FromLiteral("Weather"),
					HasConfiguration: true,
					DataSchema: "{}")),
			new WidgetTypeCatalogEntry(WidgetTypeIds.Clock,
				string.Empty,
				new WidgetTypeDescriptor(WidgetTypeIds.Clock, LocalizedText.FromLiteral("Clock"))));
		var controller = CreateController(new GetWidgetTypesRequestMessageHandler(registry));

		var response = await controller.GetTypes(CancellationToken.None);

		var weather = response.Types.Single(type => type.Id == WidgetTypeIds.Weather);
		var clock = response.Types.Single(type => type.Id == WidgetTypeIds.Clock);

		Assert.Multiple(() =>
		{
			Assert.That(weather.SupportsConfigUi, Is.True);
			Assert.That(weather.ConfigUiModelVersion,
				Is.EqualTo(UiModelVersions.Current),
				"a type that supports a config tree must report the current UI model major, not a literal");
			Assert.That(clock.SupportsConfigUi, Is.False);
			Assert.That(clock.ConfigUiModelVersion,
				Is.Zero,
				"the version is only meaningful once supportsConfigUi is set");
		});
	}

	[Test]
	public async Task GetTypes_includes_a_provider_contributed_type_alongside_the_built_ins()
	{
		var registry = new FakeWidgetTypeRegistry(new WidgetTypeCatalogEntry(WidgetTypeIds.ActionButton,
				string.Empty,
				new WidgetTypeDescriptor(WidgetTypeIds.ActionButton, LocalizedText.FromLiteral("Action Button"))),
			new WidgetTypeCatalogEntry("com.example.gauges::gauge",
				"com.example.gauges",
				new WidgetTypeDescriptor("gauge", LocalizedText.FromLiteral("Gauge"))));
		var controller = CreateController(new GetWidgetTypesRequestMessageHandler(registry));

		var response = await controller.GetTypes(CancellationToken.None);

		var gauge = response.Types.Single(type => type.Id == "com.example.gauges::gauge");
		Assert.Multiple(() =>
		{
			Assert.That(gauge.ProviderId, Is.EqualTo("com.example.gauges"));
			Assert.That(gauge.IsBuiltIn, Is.False);
			Assert.That(response.Types.Any(type => type.Id == WidgetTypeIds.ActionButton), Is.True);
		});
	}

	private sealed class FakeWidgetTypeRegistry : IWidgetTypeRegistry
	{
		private readonly List<WidgetTypeCatalogEntry> _entries;

		public FakeWidgetTypeRegistry(params WidgetTypeCatalogEntry[] entries) => _entries = [.. entries];

		public IReadOnlyList<WidgetTypeCatalogEntry> All => _entries;

		public bool IsRegistered(string? id) => _entries.Any(entry => entry.WidgetTypeId == id);

		public string? Resolve(string? spelling)
			=> _entries.FirstOrDefault(entry => entry.WidgetTypeId == spelling)?.WidgetTypeId;

		public bool TryResolve(string? widgetTypeId, out WidgetTypeCatalogEntry entry)
		{
			entry = _entries.FirstOrDefault(candidate => candidate.WidgetTypeId == widgetTypeId)!;
			return entry is not null;
		}

		public Task<WidgetTypeRegistration> Register(
			string ownerId,
			WidgetTypeDescriptor widgetType,
			CancellationToken cancellationToken = default)
			=> throw new NotSupportedException("This endpoint only reads the catalog.");

		public Task Unregister(string ownerId, string localId, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException("This endpoint only reads the catalog.");

		public Task UnregisterAll(string ownerId, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException("This endpoint only reads the catalog.");
	}
}
