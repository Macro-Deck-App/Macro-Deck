using MacroDeck.Plugin.Hosting.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Variables;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Variables;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.PluginContractTests;

/// <summary>
/// The catalog half of the <c>variables</c> capability (issue #760, merged into one provider contract by
/// ADR 0081) exercised the way the issue's own foobar2000 / Home Assistant example describes it: a
/// provider whose resources are runtime data the host never enumerates up front, browsed as a tree,
/// resolved even for an id no browse ever returned, and read or subscribed one resource at a time.
/// </summary>
[TestFixture]
internal sealed class VariableCatalogContractTests : CapabilityContractFixture
{
	private const string EntityId = "entity/sensor.office_temperature";
	private const string StateId = "entity/sensor.office_temperature/state";
	private const string UnitId = "entity/sensor.office_temperature/unit_of_measurement";
	private const string CustomTagId = "tag/MY_CUSTOM_FIELD";

	/// <summary>A catalog-only provider declares no capability of its own - its resource ids are far too
	/// numerous to declare and travel inside operation arguments instead - so the connection is opened
	/// with an empty declared set and the kind accepted at negotiation.</summary>
	private static IReadOnlyList<DeclaredCapability> NoDeclarations() => [];

	private static VariablesCapabilityHandler Handler(IPluginIntegration provider)
		=> new([provider],
			TestMetadata.Default,
			new VariableSubscriptions(Serilog.Log.Logger),
			Serilog.Core.Logger.None);

	private static VariableDefinition OnDemand(string id, string name, VariableType type)
		=> new()
		{
			Id = id, Name = name, Type = type, Materialization = VariableMaterialization.OnDemand
		};

	[Test]
	public async Task Describe_reaches_the_snapshot()
	{
		var provider = new TestVariableCatalogIntegration("Home Assistant",
			supportsPush: true,
			supportsSearch: true);
		var integration = await ConnectAsync([Handler(provider)],
			NoDeclarations(),
			[CapabilityKinds.Variables]);

		var remote = (IVariableProvider)integration;

		Assert.Multiple(() =>
		{
			Assert.That(remote.SupportsCatalog, Is.True);
			Assert.That(remote.CatalogName, Is.EqualTo("Home Assistant"));
			Assert.That(remote.SupportsPush, Is.True);
			Assert.That(remote.SupportsSearch, Is.True);
			Assert.That(IntegrationCapabilityValidator.Validate(integration), Is.Empty);
		});
	}

	[Test]
	public async Task The_root_page_contains_a_container_whose_children_round_trip_their_parent_id()
	{
		var provider = new TestVariableCatalogIntegration("Home Assistant",
			discover: (query, _) => ValueTask.FromResult(query.ParentId switch
			{
				null => new VariableCatalogPage
				{
					Items =
					[
						OnDemand(EntityId, "office_temperature", VariableType.Numeric) with
						{
							IsContainer = true, IsBindable = false
						}
					]
				},
				EntityId => new VariableCatalogPage
				{
					Items =
					[
						OnDemand(StateId, "state", VariableType.Numeric) with { ParentId = EntityId },
						OnDemand(UnitId, "unit_of_measurement", VariableType.Text) with { ParentId = EntityId }
					]
				},
				_ => VariableCatalogPage.Empty
			}));

		var integration = await ConnectAsync([Handler(provider)],
			NoDeclarations(),
			[CapabilityKinds.Variables]);

		var remote = (IVariableProvider)integration;

		var root = await remote.DiscoverAsync(new VariableCatalogQuery(), CancellationToken.None);
		var rootEntry = root.Items.Single(item => item.Id == EntityId);

		var children = await remote.DiscoverAsync(new VariableCatalogQuery { ParentId = EntityId },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(rootEntry.IsContainer, Is.True);
			Assert.That(children.Items.Select(item => item.Id), Is.EquivalentTo(new[] { StateId, UnitId }));
			Assert.That(children.Items, Has.All.Matches<VariableDefinition>(item => item.ParentId == EntityId));
		});
	}

	[Test]
	public async Task Paging_over_three_items_with_page_size_one_yields_three_pages_with_no_duplicates_or_omissions()
	{
		string[] ids = ["a", "b", "c"];
		var provider = new TestVariableCatalogIntegration("Flat Provider",
			discover: (query, _) =>
			{
				var start = query.ContinuationToken is { } token
					? int.Parse(token, System.Globalization.CultureInfo.InvariantCulture)
					: 0;
				var slice = ids.Skip(start).Take(query.PageSize).ToList();
				var next = start + slice.Count;

				return ValueTask.FromResult(new VariableCatalogPage
				{
					Items = [.. slice.Select(id => OnDemand(id, id, VariableType.Text))],
					ContinuationToken = next < ids.Length
						? next.ToString(System.Globalization.CultureInfo.InvariantCulture)
						: null
				});
			});

		var integration = await ConnectAsync([Handler(provider)],
			NoDeclarations(),
			[CapabilityKinds.Variables]);

		var remote = (IVariableProvider)integration;

		var seen = new List<string?>();
		string? cursor = null;
		var pages = 0;

		do
		{
			var page = await remote.DiscoverAsync(new VariableCatalogQuery { PageSize = 1, ContinuationToken = cursor },
				CancellationToken.None);
			pages++;
			seen.AddRange(page.Items.Select(item => item.Id));
			cursor = page.ContinuationToken;
		} while (cursor is not null && pages < 10);

		Assert.Multiple(() =>
		{
			Assert.That(pages, Is.EqualTo(3));
			Assert.That(seen, Is.EqualTo(ids));
			Assert.That(cursor, Is.Null);
		});
	}

	[Test]
	public async Task Resolve_succeeds_for_an_id_no_discover_call_ever_returned()
	{
		var provider = new TestVariableCatalogIntegration("foobar2000",
			resolve: (id, _) => ValueTask.FromResult(id == CustomTagId
				? OnDemand(CustomTagId, "my_custom_field", VariableType.Text)
				: null));

		var integration = await ConnectAsync([Handler(provider)],
			NoDeclarations(),
			[CapabilityKinds.Variables]);

		var remote = (IVariableProvider)integration;
		var resolved = await remote.ResolveAsync(CustomTagId, CancellationToken.None);

		Assert.That(resolved?.Id, Is.EqualTo(CustomTagId));
	}

	[Test]
	public async Task Get_of_an_unavailable_resource_is_null_and_a_numeric_one_round_trips_value_and_decimal_places()
	{
		var provider = new TestVariableCatalogIntegration("Home Assistant",
			resolve: (id, _) => ValueTask.FromResult(id == StateId
				? OnDemand(StateId, "state", VariableType.Numeric) with { DecimalPlaces = 1 }
				: null),
			read: (id, _) => ValueTask.FromResult(id == StateId
				? VariableReading.Of(21.5)
				: VariableReading.Unavailable));

		var integration = await ConnectAsync([Handler(provider)],
			NoDeclarations(),
			[CapabilityKinds.Variables]);

		var remote = (IVariableProvider)integration;

		var unavailable = (await remote.ReadAsync(UnitId, CancellationToken.None)).Value;
		var numeric = (await remote.ReadAsync(StateId, CancellationToken.None)).Value;
		var definition = await remote.ResolveAsync(StateId, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(unavailable, Is.Null);
			Assert.That(numeric, Is.EqualTo(21.5));
			Assert.That(definition?.DecimalPlaces, Is.EqualTo(1));
		});
	}

	/// <summary>The volatile attributes are the reason a read returns a reading rather than a bare scalar,
	/// so they have to survive the whole remote hop rather than being reconstructed host-side.</summary>
	[Test]
	public async Task A_reading_carries_its_volatile_range_across_the_wire()
	{
		var provider = new TestVariableCatalogIntegration("Home Assistant",
			read: (_, _) => ValueTask.FromResult(VariableReading.Of(21.5, -10, 40, 0.5)));

		var integration = await ConnectAsync([Handler(provider)],
			NoDeclarations(),
			[CapabilityKinds.Variables]);

		var reading = await ((IVariableProvider)integration).ReadAsync(StateId, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(reading.Value, Is.EqualTo(21.5));
			Assert.That(reading.Min, Is.EqualTo(-10));
			Assert.That(reading.Max, Is.EqualTo(40));
			Assert.That(reading.Step, Is.EqualTo(0.5));
		});
	}

	/// <summary>The declaration is the gate: the plugin-side handler refuses a write to a resource whose
	/// definition carries no write capability without ever calling the provider, so an unwritable
	/// resource cannot be written by a host that asks anyway.</summary>
	[Test]
	public async Task A_write_is_refused_for_a_resource_that_declares_no_write_capability()
	{
		var reached = false;
		var provider = new TestVariableCatalogIntegration("Home Assistant",
			resolve: (id, _) => ValueTask.FromResult(id == StateId
				? OnDemand(StateId, "state", VariableType.Numeric)
				: null),
			write: (_, _, _) =>
			{
				reached = true;
				return ValueTask.FromResult(VariableWriteResult.Applied());
			});

		var integration = await ConnectAsync([Handler(provider)],
			NoDeclarations(),
			[CapabilityKinds.Variables]);

		var result = await ((IVariableProvider)integration).SetValueAsync(StateId, 21.5, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.NotWritable));
			Assert.That(reached, Is.False, "the provider must never be asked to apply a refused write");
		});
	}

	[Test]
	public async Task A_write_to_a_writable_resource_reaches_the_provider_and_its_status_round_trips()
	{
		object? written = null;
		var provider = new TestVariableCatalogIntegration("Home Assistant",
			resolve: (id, _) => ValueTask.FromResult(id == StateId
				? OnDemand(StateId, "state", VariableType.Numeric) with { Write = new VariableWriteCapability() }
				: null),
			write: (_, value, _) =>
			{
				written = value;
				return ValueTask.FromResult(VariableWriteResult.Unavailable());
			});

		var integration = await ConnectAsync([Handler(provider)],
			NoDeclarations(),
			[CapabilityKinds.Variables]);

		var result = await ((IVariableProvider)integration).SetValueAsync(StateId, 21.5, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(written, Is.EqualTo(21.5));
			Assert.That(result.Status,
				Is.EqualTo(VariableWriteStatus.Unavailable),
				"the provider's own refusal reason has to survive the hop, not collapse into a generic failure");
		});
	}

	[Test]
	public async Task Only_a_currently_subscribed_id_reaches_the_host_sink_and_a_resubscribe_replaces_the_working_set()
	{
		var hostSubscriptions = new RemoteVariableSubscriptions(Serilog.Log.Logger);
		var channel = new VariableUpdateChannel();

		var services = new ServiceCollection().BuildServiceProvider();
		var router = new PluginCallbackRouter(SessionRegistry,
			Invoker,
			services.GetRequiredService<IServiceScopeFactory>(),
			Notifications,
			new CallbackFakeDeckNavigator(),
			new CallbackFakeScriptApi(),
			new CallbackFakeWidgetApi(),
			new CallbackFakeWidgetIconInvalidator(),
			new CallbackFakeUserVariableApi(),
			new CallbackFakeActionInteractions(),
			new RecordingUiSessionSink(),
			DeviceRegistry,
			new LayoutRegistry(Mediator),
			new FolderViewRegistry(Mediator),
			new WidgetTypeRegistry(Mediator),
			new ModalInteractionCoordinator(TimeProvider.System),
			new NullUiTransport(),
			new HostCallbackThrottle(TimeProvider.System, capacity: 1000, refillPerSecond: 1000),
			new CallbackFakeHostLockState(),
			Serilog.Core.Logger.None,
			variableSubscriptions: hostSubscriptions,
			dynamicVariableChannel: channel,
			dynamicVariableInvalidation: new VariableCatalogInvalidationSignal());

		HostInvokeHandler = (_, payload, cancellationToken)
			=> router.RouteAsync(PluginId, Guid.NewGuid().ToString(), payload, cancellationToken);

		var provider = new TestVariableCatalogIntegration("Test Provider");

		var integration = await ConnectAsync([Handler(provider)],
			NoDeclarations(),
			[CapabilityKinds.Variables]);

		((RemotePluginIntegration)integration).VariableSubscriptions = hostSubscriptions;

		var remote = (IVariableProvider)integration;
		await remote.SubscribeAsync(["A", "B"], CancellationToken.None);

		// Simulated directly against the plugin's own host invoker rather than through IVariableSink, so
		// this proves the *host's* defense specifically: even a push that never went through the
		// plugin-side sink's own filtering must still be policed on arrival.
		await CreatePluginHostInvoker().InvokeAsync(HostApis.VariableValues,
			HostOperations.VariableValues.Value,
			new VariableValuesValueArguments
			{
				Values =
				[
					new VariableIdValueDto
					{
						Id = "A",
						Reading = new VariableReadingDto { Value = new VariableValueDto { Kind = "text", Text = "a" } }
					},
					new VariableIdValueDto
					{
						Id = "C",
						Reading = new VariableReadingDto { Value = new VariableValueDto { Kind = "text", Text = "c" } }
					}
				]
			},
			CancellationToken.None);

		var drained = channel.DrainAvailable();

		Assert.Multiple(() =>
		{
			Assert.That(drained.Select(update => update.LocalResourceId), Is.EqualTo(new[] { "A" }));
			Assert.That(drained.Single().Value, Is.EqualTo("a"));
		});

		await remote.SubscribeAsync(["B"], CancellationToken.None);

		Assert.That(provider.SubscribeCalls.Last(), Is.EquivalentTo(new[] { "B" }));
	}
}
