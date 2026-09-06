using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using SdkVariableType = MacroDeck.Sdk.Variables.VariableType;
using MacroDeckHost.Tests.UnitTests.Variables;
using VariableCatalogPage = MacroDeck.Sdk.Variables.VariableCatalogPage;
using VariableDefinition = MacroDeck.Sdk.Variables.VariableDefinition;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[TestFixture]
public class VariableCatalogHandlersTests
{
	private static VariableDefinition Item(string id, bool bindable = true) => new()
	{
		Id = id,
		Name = id,
		Type = SdkVariableType.Text,
		Materialization = MacroDeck.Sdk.Variables.VariableMaterialization.OnDemand,
		IsBindable = bindable,
	};

	[Test]
	public async Task Discovery_pages_through_a_large_provider()
	{
		var items = Enumerable.Range(0, 250).Select(i => Item($"id-{i}")).ToList();
		var provider = new FakeVariableProviderIntegration { AllItems = items };
		var registry = new ConfigurableIntegrationRegistry([provider]);
		var handler = new DiscoverCatalogVariablesRequestMessageHandler(new VariableCatalogProviders(registry),
			new VariableRegistry());

		var seenIds = new HashSet<string>();
		string? cursor = null;
		DiscoverCatalogVariablesResponse? last = null;

		for (var page = 0; page < 3; page++)
		{
			var response = await handler.Handle(new DiscoverCatalogVariablesRequest
					{ IntegrationId = provider.Id, Limit = 100, Cursor = cursor },
				CancellationToken.None);

			foreach (var node in response.Nodes)
			{
				seenIds.Add(node.Id);
			}

			// The cursor must round-trip verbatim: whatever the provider handed back as its
			// continuation token is exactly what the next request carries.
			Assert.That(provider.ReceivedQueries[^1].ContinuationToken, Is.EqualTo(cursor));

			cursor = response.NextCursor;
			last = response;
		}

		Assert.Multiple(() =>
		{
			Assert.That(seenIds, Has.Count.EqualTo(250));
			Assert.That(last!.HasMore, Is.False);
			Assert.That(last.NextCursor, Is.Null);
		});
	}

	[Test]
	public async Task Search_is_delegated_to_the_provider_and_not_post_filtered()
	{
		var provider = new FakeVariableProviderIntegration
		{
			SupportsSearch = true,
			DiscoverHandler = query => query.Search == "kitchen"
				? new VariableCatalogPage { Items = [Item("special-only-via-search")] }
				: new VariableCatalogPage { Items = [Item("root-item")] },
		};
		var registry = new ConfigurableIntegrationRegistry([provider]);
		var handler = new DiscoverCatalogVariablesRequestMessageHandler(new VariableCatalogProviders(registry),
			new VariableRegistry());

		var response = await handler.Handle(
			new DiscoverCatalogVariablesRequest { IntegrationId = provider.Id, Search = "kitchen" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(provider.ReceivedQueries[0].Search, Is.EqualTo("kitchen"));
			Assert.That(response.Nodes.Select(n => n.Id), Does.Contain("special-only-via-search"));

			// No un-searched page ever contained this item - proof the host returned exactly what the
			// provider answered rather than filtering a page fetched without a search term.
			Assert.That(response.Nodes.Select(n => n.Id), Does.Not.Contain("root-item"));
		});
	}

	[Test]
	public async Task Search_is_not_offered_for_a_provider_that_does_not_support_it()
	{
		var provider = new FakeVariableProviderIntegration { SupportsSearch = false };
		var registry = new ConfigurableIntegrationRegistry([provider]);
		var providersHandler = new GetVariableCatalogProvidersRequestMessageHandler(
			new VariableCatalogProviders(registry),
			registry,
			new VariableBindingLookup(new VariableRegistry(), new InMemoryVariableBindingStore()),
			ReadyStartup());
		var discoverHandler = new DiscoverCatalogVariablesRequestMessageHandler(new VariableCatalogProviders(registry),
			new VariableRegistry());

		var providersResponse = await providersHandler.Handle(new GetVariableCatalogProvidersRequest(),
			CancellationToken.None);
		await discoverHandler.Handle(new DiscoverCatalogVariablesRequest
				{ IntegrationId = provider.Id, Search = "anything" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(providersResponse.Providers.Single().SupportsSearch, Is.False);

			// The user-typed search must never reach a provider that cannot honour it - filtering one
			// fetched page locally would silently hide most matches.
			Assert.That(provider.ReceivedQueries[0].Search, Is.Null);
		});
	}

	[Test]
	public async Task Every_provider_reports_manual_id_support()
	{
		// Every catalog provider must implement ResolveAsync, so the client's manual-id input (issue
		// #760's foobar2000 case) is always a valid affordance - the client gates it on this flag, and
		// a DTO/client drift here silently deletes the whole affordance rather than throwing (see
		// variable-catalog.service.ts for the counterpart).
		var provider = new FakeVariableProviderIntegration();
		var registry = new ConfigurableIntegrationRegistry([provider]);
		var providersHandler = new GetVariableCatalogProvidersRequestMessageHandler(
			new VariableCatalogProviders(registry),
			registry,
			new VariableBindingLookup(new VariableRegistry(), new InMemoryVariableBindingStore()),
			ReadyStartup());

		var response = await providersHandler.Handle(new GetVariableCatalogProvidersRequest(),
			CancellationToken.None);

		Assert.That(response.Providers.Single().SupportsManualIds, Is.True);
	}

	[Test]
	public async Task An_offline_provider_reports_unavailable_rather_than_empty()
	{
		var registry = new ConfigurableIntegrationRegistry([]);
		var handler = new DiscoverCatalogVariablesRequestMessageHandler(new VariableCatalogProviders(registry),
			new VariableRegistry());

		var response = await handler.Handle(new DiscoverCatalogVariablesRequest { IntegrationId = "not-registered" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Available, Is.False);
			Assert.That(response.Nodes, Is.Empty);
		});
	}

	[Test]
	public async Task Already_bound_nodes_carry_their_variable_id()
	{
		const string integrationId = "home-assistant";
		var provider = new FakeVariableProviderIntegration
		{
			Id = integrationId,
			AllItems = [Item("light.kitchen"), Item("light.bedroom")],
		};
		var registry = new ConfigurableIntegrationRegistry([provider]);
		var variableRegistry = new VariableRegistry();
		var boundVariableId = Guid.NewGuid();
		variableRegistry.Upsert(new VariableEntity
		{
			Id = boundVariableId,
			Name = "kitchen_light",
			Scope = VariableScope.Global,
			Type = VariableType.Text,
			Classification = VariableClassification.Integration,
			OwnerIntegrationId = integrationId,
			DefinitionId = "light.kitchen",
		});

		var handler = new DiscoverCatalogVariablesRequestMessageHandler(new VariableCatalogProviders(registry),
			variableRegistry);

		var response = await handler.Handle(new DiscoverCatalogVariablesRequest { IntegrationId = integrationId },
			CancellationToken.None);

		var kitchen = response.Nodes.Single(n => n.Id == "light.kitchen");
		var bedroom = response.Nodes.Single(n => n.Id == "light.bedroom");

		Assert.Multiple(() =>
		{
			Assert.That(kitchen.BoundVariableId, Is.EqualTo(boundVariableId.ToString()));
			Assert.That(bedroom.BoundVariableId, Is.Null);
		});
	}

	[Test]
	public async Task Binding_a_non_bindable_node_is_refused()
	{
		var bindings = new FakeVariableBindingService
		{
			OnBind = (_, _, _, _) => Result.Fail<VariableEntity, VariableBindingError>(VariableBindingError.NotBindable,
				"'group.kitchen' is not bindable"),
		};
		var handler = new BindCatalogVariableRequestMessageHandler(bindings);

		var response = await handler.Handle(new BindCatalogVariableRequest
				{ IntegrationId = "home-assistant", ResourceId = "group.kitchen" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Variable, Is.Null);
			Assert.That(response.Error, Is.Not.Null);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(VariableBindingError.NotBindable)));
		});
	}

	[Test]
	public async Task A_limit_above_the_protocol_bound_is_clamped()
	{
		var provider = new FakeVariableProviderIntegration();
		var registry = new ConfigurableIntegrationRegistry([provider]);
		var handler = new DiscoverCatalogVariablesRequestMessageHandler(new VariableCatalogProviders(registry),
			new VariableRegistry());

		await handler.Handle(new DiscoverCatalogVariablesRequest
			{
				IntegrationId = provider.Id, Limit = ProtocolLimits.MaxVariableCatalogPageSize + 1000,
			},
			CancellationToken.None);

		Assert.That(provider.ReceivedQueries[0].PageSize, Is.EqualTo(ProtocolLimits.MaxVariableCatalogPageSize));
	}

	private static StartupReadiness ReadyStartup()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return readiness;
	}
}
