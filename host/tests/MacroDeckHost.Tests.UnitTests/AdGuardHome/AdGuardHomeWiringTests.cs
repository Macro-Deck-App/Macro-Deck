using System.Text.Json;
using MacroDeckHost.Application.AdGuardHome;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.AdGuardHome;
using MacroDeckHost.Tests.UnitTests.Calendar;
using MacroDeckHost.Tests.UnitTests.Delegation;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.AdGuardHome;

[TestFixture]
internal sealed class AdGuardHomeWiringTests
{
	[Test]
	public async Task The_host_hub_reaches_the_integration_through_the_gateway_binder()
	{
		var hub = new AdGuardHomeHub();
		var integration = new AdGuardHomeIntegration(_ => new FakeAdGuardHomeClient(), new FakeTimeProvider());
		var context = new FakeAdGuardHomeContext();
		context.AddEntry("Home", new Dictionary<string, string?> { [AdGuardHomeConfigKeys.BaseUrl] = "http://home.lan" });

		IntegrationGatewayBinder.Bind(integration,
			null!,
			null!,
			new NullBindingStore(),
			new VariableRefreshSignal(),
			adGuardHomeSink: hub);
		await integration.InitializeAsync(context);

		try
		{
			await CalendarTree.WaitForAsync(() => hub.Instances is [{ IsConnected: true }], "the hub never got the instance");
		}
		finally
		{
			await integration.ShutdownAsync();
		}
	}

	[Test]
	public void A_renamed_instance_keeps_its_variable_key()
	{
		var adapter = Adapter();
		var existing = Record("Home", key: "home");

		var result = adapter.Prepare(Preparation("Home DNS", existing.Id, existing, [existing], titleChanged: true));

		Assert.That(Key(result), Is.EqualTo("home"));
	}

	[Test]
	public void New_instances_with_the_same_name_get_distinct_variable_keys()
	{
		var adapter = Adapter();
		var sibling = Record("Home", key: "home");

		var result = adapter.Prepare(Preparation("Home", Guid.NewGuid(), null, [sibling], titleChanged: false));

		Assert.That(Key(result), Is.EqualTo("home_2"));
	}

	[TestCase(AdGuardHomeConnection.Connected, IntegrationConfigEntryStatus.Connected)]
	[TestCase(AdGuardHomeConnection.Connecting, IntegrationConfigEntryStatus.Connecting)]
	[TestCase(AdGuardHomeConnection.Unauthorized, IntegrationConfigEntryStatus.NeedsReconfiguration)]
	[TestCase(AdGuardHomeConnection.Unreachable, IntegrationConfigEntryStatus.Disconnected)]
	[TestCase(AdGuardHomeConnection.Incompatible, IntegrationConfigEntryStatus.Disconnected)]
	public void The_entry_status_reflects_the_instance_connection(
		AdGuardHomeConnection connection,
		IntegrationConfigEntryStatus expected)
	{
		var hub = new AdGuardHomeHub();
		var record = Record("Home", key: "home");
		hub.Replace([new AdGuardHomeSnapshot(record.Id.ToString("D"), "Home", "home", connection)]);

		Assert.That(Adapter(hub).GetStatus(record), Is.EqualTo(expected));
	}

	[Test]
	public void Variables_of_a_deleted_instance_are_removed_and_the_others_kept()
	{
		var office = Record("Office", key: "office");
		var kept = Variable("adguard-home-office-dns-queries");
		var orphaned = Variable("adguard-home-home-dns-queries");
		var foreign = Variable("something-else");

		var removed = AdGuardHomeConfigurationMutationAdapter.OrphanedVariables([kept, orphaned, foreign], [office]);

		Assert.That(removed, Is.EqualTo(new[] { orphaned.Id }));
	}

	[Test]
	public void Nothing_is_removed_while_an_entry_has_no_stored_variable_key()
	{
		var legacy = new ConfigEntryRecord(Guid.NewGuid(), AdGuardHomeIntegration.IntegrationId, "Home", DateTime.UtcNow,
			new Dictionary<string, JsonElement>());

		Assert.That(AdGuardHomeConfigurationMutationAdapter.OrphanedVariables(
			[Variable("adguard-home-home-2-dns-queries")], [legacy]), Is.Empty);
	}

	[Test]
	public void Deleting_the_last_instance_removes_all_of_its_variables()
	{
		var variable = Variable("adguard-home-home-is-reachable");

		Assert.That(AdGuardHomeConfigurationMutationAdapter.OrphanedVariables([variable], []),
			Is.EqualTo(new[] { variable.Id }));
	}

	private static VariableEntity Variable(string definitionId)
		=> new()
		{
			Id = Guid.NewGuid(),
			Name = definitionId.Replace('-', '_'),
			Scope = MacroDeckHost.Domain.Enums.VariableScope.Global,
			Type = MacroDeckHost.Domain.Enums.VariableType.Numeric,
			Classification = MacroDeckHost.Domain.Enums.VariableClassification.Integration,
			DefinitionId = definitionId,
		};

	private static AdGuardHomeConfigurationMutationAdapter Adapter(AdGuardHomeHub? hub = null)
		=> new(new FakeIntegrationRegistry(), hub ?? new AdGuardHomeHub(),
			new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider()
				.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>());

	private static ConfigEntryRecord Record(string title, string key)
		=> new(Guid.NewGuid(),
			AdGuardHomeIntegration.IntegrationId,
			title,
			DateTime.UtcNow,
			new Dictionary<string, JsonElement>
			{
				[AdGuardHomeConfigKeys.VariableKey] = JsonSerializer.SerializeToElement(key)
			});

	private static IntegrationConfigMutationPreparation Preparation(
		string title,
		Guid entryId,
		ConfigEntryRecord? existing,
		IReadOnlyList<ConfigEntryRecord> siblings,
		bool titleChanged)
		=> new(AdGuardHomeIntegration.IntegrationId,
			entryId,
			title,
			new Dictionary<string, JsonElement>(),
			existing,
			siblings,
			titleChanged);

	private static string? Key(IntegrationConfigMutationPreparationResult result)
		=> result.Values[AdGuardHomeConfigKeys.VariableKey].GetString();

	private sealed class NullBindingStore : IVariableBindingStore
	{
		public IReadOnlyList<VariableBinding> Load() => [];

		public bool TryLoad(out IReadOnlyList<VariableBinding> bindings)
		{
			bindings = [];
			return true;
		}

		public bool Save(IEnumerable<VariableBinding> bindings) => true;
	}
}
