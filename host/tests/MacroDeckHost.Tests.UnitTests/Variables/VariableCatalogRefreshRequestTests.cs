using System.Collections.Concurrent;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
internal sealed class VariableCatalogRefreshRequestTests
{
	private const string IntegrationId = "com.example.mixer";
	private const string MutedId = "input/mic/muted";

	private static VariableDefinition Muted => new()
	{
		Id = MutedId,
		Name = "mic_muted",
		Type = VariableType.Boolean,
		Materialization = VariableMaterialization.OnDemand,
		RefreshInterval = TimeSpan.FromMinutes(5)
	};

	[Test]
	public async Task A_definition_request_reads_a_bound_resource_on_the_next_tick()
	{
		var provider = new FakeVariableProviderIntegration { Id = IntegrationId };
		provider.AddDefinition(Muted);
		provider.SetValue(MutedId, false);
		var harness = new VariableCatalogHarness(provider);
		await harness.BindingService.BindAsync(IntegrationId, MutedId, null, null);
		await harness.UpdateService.PollDueAsync(CancellationToken.None);
		var before = ReadsOf(provider, MutedId);

		harness.Refresh.RequestDefinitionRefresh(IntegrationId, MutedId);
		await harness.UpdateService.PollDueAsync(CancellationToken.None);

		Assert.That(ReadsOf(provider, MutedId), Is.EqualTo(before + 1));
	}

	[Test]
	public async Task A_bound_resource_is_not_read_again_before_its_interval_without_a_request()
	{
		var provider = new FakeVariableProviderIntegration { Id = IntegrationId };
		provider.AddDefinition(Muted);
		var harness = new VariableCatalogHarness(provider);
		await harness.BindingService.BindAsync(IntegrationId, MutedId, null, null);
		await harness.UpdateService.PollDueAsync(CancellationToken.None);
		var before = ReadsOf(provider, MutedId);

		await harness.UpdateService.PollDueAsync(CancellationToken.None);

		Assert.That(ReadsOf(provider, MutedId), Is.EqualTo(before));
	}

	[Test]
	public async Task A_refresh_after_a_write_still_reads_the_bound_resource_on_the_next_tick()
	{
		var provider = new FakeVariableProviderIntegration { Id = IntegrationId };
		provider.AddDefinition(Muted);
		var harness = new VariableCatalogHarness(provider);
		var bound = await harness.BindingService.BindAsync(IntegrationId, MutedId, null, null);
		await harness.UpdateService.PollDueAsync(CancellationToken.None);
		var before = ReadsOf(provider, MutedId);

		harness.Refresh.RequestRefresh(IntegrationId, bound.Data!.Id);
		await harness.UpdateService.PollDueAsync(CancellationToken.None);

		Assert.That(ReadsOf(provider, MutedId), Is.EqualTo(before + 1));
	}

	[Test]
	public async Task A_provider_that_pushes_is_not_read_for_a_request_and_the_request_does_not_linger()
	{
		var provider = new FakeVariableProviderIntegration { Id = IntegrationId, SupportsPush = true };
		provider.AddDefinition(Muted);
		var harness = new VariableCatalogHarness(provider);
		await harness.BindingService.BindAsync(IntegrationId, MutedId, null, null);
		var before = ReadsOf(provider, MutedId);

		harness.Refresh.RequestDefinitionRefresh(IntegrationId, MutedId);
		await harness.UpdateService.PollDueAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(ReadsOf(provider, MutedId), Is.EqualTo(before));
			Assert.That(harness.Refresh.PendingIntegrations(VariableRefreshLane.Catalog), Is.Empty);
		});
	}

	[Test]
	public async Task Requests_for_an_integration_with_nothing_bound_do_not_pile_up()
	{
		var provider = new FakeVariableProviderIntegration { Id = IntegrationId };
		provider.AddDefinition(Muted);
		var harness = new VariableCatalogHarness(provider);
		await harness.BindingService.BindAsync(IntegrationId, MutedId, null, null);

		harness.Refresh.RequestDefinitionRefresh("com.example.unbound", "anything/at/all");
		harness.Refresh.RequestRefresh("com.example.unbound", Guid.NewGuid());
		await harness.UpdateService.PollDueAsync(CancellationToken.None);

		Assert.That(harness.Refresh.PendingIntegrations(VariableRefreshLane.Catalog), Is.Empty);
	}

	[Test]
	public async Task A_request_that_arrives_while_a_read_is_in_flight_is_read_once_that_read_ends()
	{
		var provider = new MixedProvider();
		var harness = new VariableCatalogHarness(provider);
		await harness.BindingService.BindAsync(IntegrationId, MixedProvider.ResourceId, null, null);
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		provider.Gate = gate;

		await harness.UpdateService.PollDueAsync(CancellationToken.None);
		var started = provider.ReadsOf(MixedProvider.ResourceId);
		harness.Refresh.RequestDefinitionRefresh(IntegrationId, MixedProvider.ResourceId);
		await harness.UpdateService.PollDueAsync(CancellationToken.None);
		provider.Gate = null;
		gate.SetResult();

		await Until(async () =>
		{
			await harness.UpdateService.PollDueAsync(CancellationToken.None);
			return provider.ReadsOf(MixedProvider.ResourceId) == started + 1;
		});
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task A_request_reaches_both_loops_whichever_drains_first(bool eagerLoopFirst)
	{
		var provider = new MixedProvider();
		var harness = new VariableCatalogHarness(provider);
		await harness.BindingService.BindAsync(IntegrationId, MixedProvider.ResourceId, null, null);
		var eagerLoop = new IntegrationVariablePollingBackgroundService(new StartedHostLifetime(),
			harness.Integrations,
			harness.ScopeFactory,
			new VariablePollingInvalidationSignal(),
			harness.Refresh,
			Log.Logger);
		await harness.UpdateService.PollDueAsync(CancellationToken.None);
		await Until(async () =>
		{
			await eagerLoop.DispatchDue(CancellationToken.None);
			return provider.ReadsOf(MixedProvider.EagerId) == 1;
		});
		var resourceReads = provider.ReadsOf(MixedProvider.ResourceId);

		harness.Refresh.RequestDefinitionRefresh(IntegrationId, MixedProvider.EagerId);
		harness.Refresh.RequestDefinitionRefresh(IntegrationId, MixedProvider.ResourceId);
		if (eagerLoopFirst)
		{
			await eagerLoop.DispatchDue(CancellationToken.None);
			await harness.UpdateService.PollDueAsync(CancellationToken.None);
		}
		else
		{
			await harness.UpdateService.PollDueAsync(CancellationToken.None);
			await eagerLoop.DispatchDue(CancellationToken.None);
		}

		await Until(async () =>
		{
			await eagerLoop.DispatchDue(CancellationToken.None);
			await harness.UpdateService.PollDueAsync(CancellationToken.None);
			return provider.ReadsOf(MixedProvider.EagerId) == 2;
		});
		Assert.That(provider.ReadsOf(MixedProvider.ResourceId), Is.EqualTo(resourceReads + 1));
	}

	private static int ReadsOf(FakeVariableProviderIntegration provider, string id)
	{
		return provider.GetValueCalls.Count(call => call == id);
	}

	private static async Task Until(Func<Task<bool>> done)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (!await done())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("the expected reads never happened");
			}

			await Task.Delay(10);
		}
	}

	private sealed class MixedProvider : IIntegration, IVariableProvider
	{
		public const string EagerId = "first";
		public const string ResourceId = "input/mic/volume";

		private readonly ConcurrentDictionary<string, int> _reads = new(StringComparer.Ordinal);

		public TaskCompletionSource? Gate { get; set; }

		public string Id => IntegrationId;

		public LocalizedText Name => "Mixer";

		public string Version => "1.0.0";

		public IReadOnlyList<IActionDefinition> Actions => [];

		public bool IsInitialized => true;

		public IReadOnlyList<VariableDefinition> Variables { get; } =
			[VariableDefinition.Eager(EagerId, VariableType.Text, refreshInterval: TimeSpan.FromMinutes(5))];

		public bool SupportsCatalog => true;

		public int ReadsOf(string id)
		{
			return _reads.GetValueOrDefault(id);
		}

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}

		public ValueTask<VariableCatalogPage> DiscoverAsync(
			VariableCatalogQuery query,
			CancellationToken cancellationToken = default)
		{
			return ValueTask.FromResult(VariableCatalogPage.Empty);
		}

		public ValueTask<VariableDefinition?> ResolveAsync(
			string localId,
			CancellationToken cancellationToken = default)
		{
			return ValueTask.FromResult(localId == ResourceId
				? new VariableDefinition
				{
					Id = ResourceId,
					Name = "mic_volume",
					Type = VariableType.Numeric,
					Materialization = VariableMaterialization.OnDemand,
					RefreshInterval = TimeSpan.FromMinutes(5)
				}
				: null);
		}

		public async ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
		{
			_reads.AddOrUpdate(localId, 1, static (_, count) => count + 1);
			if (localId == ResourceId && Gate is { } gate)
			{
				await gate.Task;
			}

			return VariableReading.Of(localId == ResourceId ? 50d : "value");
		}
	}
}
