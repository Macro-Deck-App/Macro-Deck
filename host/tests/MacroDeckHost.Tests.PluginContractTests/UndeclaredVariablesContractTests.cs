using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class UndeclaredVariablesContractTests : CapabilityContractFixture
{
	private const string LevelId = "device-level";

	private static readonly VariableDefinition _level = new()
	{
		Id = LevelId,
		Name = "level",
		Type = VariableType.Numeric,
		Materialization = VariableMaterialization.OnDemand,
		Write = new VariableWriteCapability()
	};

	[Test]
	public async Task A_catalog_only_provider_offers_its_catalog_after_connecting()
	{
		var writes = new List<string>();
		var provider = new TestVariableCatalogIntegration("Devices",
			discover: (_, _) => ValueTask.FromResult(new VariableCatalogPage { Items = [_level] }),
			resolve: (id, _) => ValueTask.FromResult<VariableDefinition?>(id == LevelId ? _level : null),
			write: (id, value, _) =>
			{
				writes.Add($"{id}={value}");
				return ValueTask.FromResult(VariableWriteResult.Applied());
			});

		await ConnectAsTheSdkDeclaresAsync([Handler(provider)]);

		var providers = new VariableCatalogProviders(IntegrationRegistry);
		var catalog = providers.Resolve(PluginId);
		Assert.That(catalog, Is.Not.Null, "a catalog-only provider must be offered as a catalog");

		var page = await catalog!.DiscoverAsync(new VariableCatalogQuery(), CancellationToken.None);
		var result = await providers.ResolveOwner(PluginId)!.SetValueAsync(LevelId, 5.0, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(catalog.CatalogName, Is.EqualTo("Devices"));
			Assert.That(page.Items.Select(item => item.Id), Is.EqualTo(new[] { LevelId }));
			Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.Applied));
			Assert.That(writes, Is.EqualTo(new[] { $"{LevelId}=5" }));
		});
	}

	[Test]
	public async Task A_provider_with_only_on_demand_variables_owns_them_after_connecting()
	{
		await ConnectAsTheSdkDeclaresAsync([Handler(new OnDemandOnlyIntegration())]);

		var owner = new VariableCatalogProviders(IntegrationRegistry).ResolveOwner(PluginId);

		Assert.That(owner, Is.Not.Null);
		Assert.That(owner!.DeclaredVariables.Select(variable => variable.Id), Is.EqualTo(new[] { LevelId }));
	}

	[Test]
	public async Task A_plugin_without_variables_registers_without_offering_them()
	{
		await ConnectAsTheSdkDeclaresAsync([new ImmediateActionsHandler()]);

		Assert.Multiple(() =>
		{
			Assert.That(new VariableCatalogProviders(IntegrationRegistry).ResolveOwner(PluginId), Is.Null);
			Assert.That(SnapshotStore.GetSnapshot(PluginId).AcceptedKinds,
				Does.Not.Contain(CapabilityKinds.Variables));
		});
	}

	[Test]
	public async Task Variables_remembered_from_an_earlier_connection_are_not_offered_by_a_plugin_that_dropped_them()
	{
		await SnapshotStore.SaveAsync(RemotePluginCapabilitySnapshot.Empty(PluginId) with
		{
			SupportsVariableCatalog = true,
			VariableCatalogName = "Devices",
			DeclaredVariables = [_level]
		});
		Assert.That(SnapshotStore.GetSnapshot(PluginId).SupportsVariableCatalog, Is.True);

		await ConnectAsTheSdkDeclaresAsync([new ImmediateActionsHandler()]);

		Assert.That(new VariableCatalogProviders(IntegrationRegistry).Resolve(PluginId), Is.Null);
	}

	[Test]
	public async Task A_plugin_that_never_answers_still_registers_once_the_probe_budget_is_spent()
	{
		var silent = new SilentVariablesHandler();

		await ConnectAsTheSdkDeclaresAsync([silent],
			async () =>
			{
				await silent.Asked.Task.WaitAsync(TimeSpan.FromSeconds(10));
				Time.Advance(RemotePluginSnapshotRefresher.ProbeTimeout);
			});

		Assert.Multiple(() =>
		{
			Assert.That(IntegrationRegistry.Integrations.Any(integration => integration.Id == PluginId), Is.True);
			Assert.That(new VariableCatalogProviders(IntegrationRegistry).ResolveOwner(PluginId), Is.Null);
		});
	}

	private static VariablesCapabilityHandler Handler(IPluginIntegration provider)
	{
		return new VariablesCapabilityHandler([provider],
			TestMetadata.Default,
			new VariableSubscriptions(Serilog.Log.Logger),
			Serilog.Core.Logger.None);
	}

	private sealed class OnDemandOnlyIntegration : IPluginIntegration, IVariableProvider
	{
		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public IReadOnlyList<VariableDefinition> Variables { get; } = [];

		public IReadOnlyList<VariableDefinition> DeclaredVariables { get; } = [_level];

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}

		public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
		{
			return ValueTask.FromResult(VariableReading.Of(1.0));
		}
	}

	private sealed class ImmediateActionsHandler : ICapabilityHandler
	{
		private const string DescribeJson =
			"""{"actions":[{"localId":"noop","name":"Noop","description":"Does nothing","parameters":[],"supportsDynamicOptions":false,"providesState":false}]}""";

		public string Kind => CapabilityKinds.Actions;

		public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		{
			return
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.Actions,
					LocalId = "noop",
					VersionRange = new CapabilityVersionRange { Minimum = 3, Maximum = 3 }
				}
			];
		}

		public Task<CapabilityInvocationResult> InvokeAsync(
			CapabilityInvocation invocation,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(CapabilityInvocationResult.Ok(JsonDocument.Parse(DescribeJson).RootElement.Clone()));
		}
	}

	private sealed class SilentVariablesHandler : ICapabilityHandler
	{
		public TaskCompletionSource Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public string Kind => CapabilityKinds.Variables;

		public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		{
			return [];
		}

		public async Task<CapabilityInvocationResult> InvokeAsync(
			CapabilityInvocation invocation,
			CancellationToken cancellationToken)
		{
			Asked.TrySetResult();
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.Cancelled, "never answered");
		}
	}
}
